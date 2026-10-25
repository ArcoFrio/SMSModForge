using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SMSModForge.Localization;

namespace SMSModForge.View.Controls;

/// <summary>
/// The bar over a list - every tab's, and the lists inside them (the author,
/// 2026-10-21: "normalize all tabs' left navigators ... so none behave
/// differently unless they have functions that should be specific to that
/// tab").
/// <para/>
/// The same buttons in the same order: Add (and its variants, marked
/// <see cref="AfterAddProperty"/>), Add folder, Rename, Duplicate, Delete, then
/// whatever only that list has. A button the list has no use for is given no
/// command and is not there. Under each button, small, the key that does the
/// same - the button itself stays its size - and at the end a keyboard sign
/// whose tip lists the keys that have no button.
/// <para/>
/// The keys are bound here too, on <see cref="List"/>, from the same commands
/// as the buttons: what a key cap says is what the key does, by construction.
/// They answer only while the list has the keyboard, so Del cannot remove a
/// record while the author is typing in its panel.
/// <para/>
/// Buttons wrap onto a second row rather than into an overflow menu: in a
/// narrow panel the menu hid Rename and Delete behind a chevron, which is not
/// "understood straight away".
/// </summary>
public sealed class NavigatorBar : ItemsControl
{
    public NavigatorBar()
    {
        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
        Focusable = false;
        Padding = new Thickness(2, 1, 2, 1);
    }

    private static DependencyProperty Register(string name, Type type, PropertyChangedCallback? changed = null)
        => DependencyProperty.Register(name, type, typeof(NavigatorBar), new PropertyMetadata(null, changed));

    public static readonly DependencyProperty AddTextProperty = Register(nameof(AddText), typeof(object));
    public static readonly DependencyProperty AddCommandProperty = Register(nameof(AddCommand), typeof(ICommand));
    public static readonly DependencyProperty AddTipProperty = Register(nameof(AddTip), typeof(object));
    public static readonly DependencyProperty AddAnchorProperty = Register(nameof(AddAnchor), typeof(string));
    public static readonly DependencyProperty FolderTextProperty = Register(nameof(FolderText), typeof(object));
    public static readonly DependencyProperty FolderCommandProperty = Register(nameof(FolderCommand), typeof(ICommand));
    public static readonly DependencyProperty FolderParameterProperty = Register(nameof(FolderParameter), typeof(object));
    public static readonly DependencyProperty FolderTipProperty = Register(nameof(FolderTip), typeof(object));
    public static readonly DependencyProperty RenameCommandProperty = Register(nameof(RenameCommand), typeof(ICommand));
    public static readonly DependencyProperty DuplicateCommandProperty = Register(nameof(DuplicateCommand), typeof(ICommand));
    public static readonly DependencyProperty DuplicateTipProperty = Register(nameof(DuplicateTip), typeof(object));
    public static readonly DependencyProperty KeysActiveProperty =
        DependencyProperty.Register(nameof(KeysActive), typeof(bool?), typeof(NavigatorBar),
                                    new PropertyMetadata(null, (d, _) => ((NavigatorBar)d).PaintCaps()));
    public static readonly DependencyProperty DeleteTextProperty = Register(nameof(DeleteText), typeof(object));
    public static readonly DependencyProperty DeleteCommandProperty = Register(nameof(DeleteCommand), typeof(ICommand));
    public static readonly DependencyProperty DeleteTipProperty = Register(nameof(DeleteTip), typeof(object));
    public static readonly DependencyProperty DeleteKeyCommandProperty = Register(nameof(DeleteKeyCommand), typeof(ICommand));
    public static readonly DependencyProperty CopyCommandProperty = Register(nameof(CopyCommand), typeof(ICommand));
    public static readonly DependencyProperty PasteCommandProperty = Register(nameof(PasteCommand), typeof(ICommand));
    public static readonly DependencyProperty ListProperty =
        Register(nameof(List), typeof(UIElement), (d, e) => ((NavigatorBar)d).BindKeys(e.NewValue as UIElement));

    public object? AddText { get => GetValue(AddTextProperty); set => SetValue(AddTextProperty, value); }
    public ICommand? AddCommand { get => (ICommand?)GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }
    public object? AddTip { get => GetValue(AddTipProperty); set => SetValue(AddTipProperty, value); }
    /// <summary>The tutorial's name for the Add button.</summary>
    public string? AddAnchor { get => (string?)GetValue(AddAnchorProperty); set => SetValue(AddAnchorProperty, value); }
    public object? FolderText { get => GetValue(FolderTextProperty); set => SetValue(FolderTextProperty, value); }
    public ICommand? FolderCommand { get => (ICommand?)GetValue(FolderCommandProperty); set => SetValue(FolderCommandProperty, value); }
    public object? FolderParameter { get => GetValue(FolderParameterProperty); set => SetValue(FolderParameterProperty, value); }
    public object? FolderTip { get => GetValue(FolderTipProperty); set => SetValue(FolderTipProperty, value); }
    public ICommand? RenameCommand { get => (ICommand?)GetValue(RenameCommandProperty); set => SetValue(RenameCommandProperty, value); }
    public ICommand? DuplicateCommand { get => (ICommand?)GetValue(DuplicateCommandProperty); set => SetValue(DuplicateCommandProperty, value); }
    /// <summary>What Duplicate does, where it is not a record beside the
    /// selected one - a node with everything under it.</summary>
    public object? DuplicateTip { get => GetValue(DuplicateTipProperty); set => SetValue(DuplicateTipProperty, value); }
    /// <summary>
    /// Whether the window's keys go to this bar's list, where a tab has two
    /// lists a key could mean - a dialogue or one of its nodes. They go to the
    /// one clicked last, and that bar's key caps are drawn in the heading
    /// colour, so which one it is can be seen rather than guessed. Null on a
    /// bar with no such twin. (Fading the other side's instead put its caps
    /// below readable in every light theme.)
    /// </summary>
    public bool? KeysActive { get => (bool?)GetValue(KeysActiveProperty); set => SetValue(KeysActiveProperty, value); }

    private readonly List<TextBlock> _caps = new();

    private void PaintCaps()
    {
        string brush = KeysActive == true ? "Theme.Heading" : "Theme.TextMuted";
        foreach (var cap in _caps) cap.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }
    public object? DeleteText { get => GetValue(DeleteTextProperty); set => SetValue(DeleteTextProperty, value); }
    public ICommand? DeleteCommand { get => (ICommand?)GetValue(DeleteCommandProperty); set => SetValue(DeleteCommandProperty, value); }
    public object? DeleteTip { get => GetValue(DeleteTipProperty); set => SetValue(DeleteTipProperty, value); }
    /// <summary>What Del does, when that is not quite the Delete button: on
    /// the Characters tab it removes whichever is selected, outfit or
    /// character, and both buttons carry the key.</summary>
    public ICommand? DeleteKeyCommand { get => (ICommand?)GetValue(DeleteKeyCommandProperty); set => SetValue(DeleteKeyCommandProperty, value); }
    public ICommand? CopyCommand { get => (ICommand?)GetValue(CopyCommandProperty); set => SetValue(CopyCommandProperty, value); }
    public ICommand? PasteCommand { get => (ICommand?)GetValue(PasteCommandProperty); set => SetValue(PasteCommandProperty, value); }
    /// <summary>The list this bar is for: its keys are bound on it.</summary>
    public UIElement? List { get => (UIElement?)GetValue(ListProperty); set => SetValue(ListProperty, value); }

    /// <summary>A button of the list's own that belongs with Add - "+ Voice",
    /// "+ Vanilla", the menu of shapes beside "+ UI" - rather than after
    /// Delete.</summary>
    public static readonly DependencyProperty AfterAddProperty =
        DependencyProperty.RegisterAttached("AfterAdd", typeof(bool), typeof(NavigatorBar), new PropertyMetadata(false));
    public static void SetAfterAdd(DependencyObject o, bool value) => o.SetValue(AfterAddProperty, value);
    public static bool GetAfterAdd(DependencyObject o) => (bool)o.GetValue(AfterAddProperty);

    /// <summary>The key cap under a button of the list's own.</summary>
    public static readonly DependencyProperty KeysProperty =
        DependencyProperty.RegisterAttached("Keys", typeof(object), typeof(NavigatorBar), new PropertyMetadata(null));
    public static void SetKeys(DependencyObject o, object? value) => o.SetValue(KeysProperty, value);
    public static object? GetKeys(DependencyObject o) => o.GetValue(KeysProperty);

    /// <summary>Every button on the bar, in order. For tests.</summary>
    internal IEnumerable<Button> Buttons
        => Items.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>());

    /// <summary>The standard buttons by role, for tests: "add", "folder",
    /// "rename", "duplicate", "delete".</summary>
    internal Button? Role(string role)
        => Buttons.FirstOrDefault(b => (string?)b.Tag == "nav:" + role);

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        var own = Items.Cast<object>().ToList();
        Items.Clear();

        Items.Add(Standard("add", AddTextProperty, AddCommandProperty, AddTipProperty, LocText.Of("navigator.addKeys"), anchor: true));
        foreach (var o in own.Where(o => o is DependencyObject d && GetAfterAdd(d))) Items.Add(Own(o));
        Items.Add(Standard("folder", FolderTextProperty, FolderCommandProperty, FolderTipProperty, null, parameter: FolderParameterProperty));
        Items.Add(Standard("rename", null, RenameCommandProperty, null, "F2", text: "navigator.rename", tip: "navigator.rename.tip"));
        var duplicate = Standard("duplicate", null, DuplicateCommandProperty, null, LocText.Of("menu.duplicateItem.keys"),
                                 text: "navigator.duplicate", tip: "navigator.duplicate.tip");
        if (DuplicateTip != null || BindingOperations.GetBindingBase(this, DuplicateTipProperty) != null)
            ((Button)duplicate.Children[0]).SetBinding(ToolTipProperty, new Binding(nameof(DuplicateTip)) { Source = this });
        Items.Add(duplicate);
        Items.Add(Standard("delete", DeleteTextProperty, DeleteCommandProperty, DeleteTipProperty, LocText.Of("menu.deleteItem.keys")));
        foreach (var o in own.Where(o => !(o is DependencyObject d && GetAfterAdd(d)))) Items.Add(Own(o));
        Items.Add(MoreKeys());
        PaintCaps();
    }

    /// <summary>A standard button, there only while it has a command.</summary>
    private StackPanel Standard(string role, DependencyProperty? textProp, DependencyProperty commandProp,
                                DependencyProperty? tipProp, object? keys, string? text = null, string? tip = null,
                                bool anchor = false, DependencyProperty? parameter = null)
    {
        var button = new Button { Tag = "nav:" + role };
        if (textProp != null) button.SetBinding(ContentControl.ContentProperty, new Binding(textProp.Name) { Source = this });
        if (text != null) LocText.Bind(button, ContentControl.ContentProperty, text);
        if (tipProp != null) button.SetBinding(ToolTipProperty, new Binding(tipProp.Name) { Source = this });
        if (tip != null) LocText.Bind(button, ToolTipProperty, tip);
        button.SetBinding(Button.CommandProperty, new Binding(commandProp.Name) { Source = this });
        if (parameter != null) button.SetBinding(Button.CommandParameterProperty, new Binding(parameter.Name) { Source = this });
        if (anchor) button.SetBinding(TutorialAnchor.IdProperty, new Binding(nameof(AddAnchor)) { Source = this });

        var slot = Slot(button, keys);
        slot.SetBinding(VisibilityProperty, new Binding(commandProp.Name)
        { Source = this, Converter = HasValue.Instance });
        return slot;
    }

    /// <summary>One of the list's own buttons (or its menu), in the same
    /// frame as the standard ones so they line up.</summary>
    private StackPanel Own(object item)
    {
        var element = item as UIElement ?? new ContentControl { Content = item };
        // A key cap written {l:T ...} arrives as a binding: handed on as one,
        // so it follows the language like the button beside it.
        object? keys = BindingOperations.GetBindingBase(element, KeysProperty) ?? GetKeys(element);
        var slot = Slot(element, keys);
        // A button that hides itself ("+ Child" under an empty conversation)
        // takes its key cap with it.
        slot.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = element });
        return slot;
    }

    private StackPanel Slot(UIElement element, object? keys)
    {
        // The bar's compact look, unless the button brings a style of its own
        // (one with triggers, based on the same look).
        if (element is Button b && b.ReadLocalValue(StyleProperty) == DependencyProperty.UnsetValue)
            b.SetResourceReference(StyleProperty, ToolBar.ButtonStyleKey);
        var cap = new TextBlock
        {
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -1, 0, 1),
            IsHitTestVisible = false,
        };
        _caps.Add(cap);
        if (keys is BindingBase binding) cap.SetBinding(TextBlock.TextProperty, binding);
        else cap.Text = keys as string ?? "";
        var slot = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        slot.Children.Add(element);
        slot.Children.Add(cap);
        return slot;
    }

    /// <summary>The keyboard sign: the keys without a button, listed in its
    /// tip, and only those this list really answers to.</summary>
    private StackPanel MoreKeys()
    {
        var sign = new TextBlock
        {
            Text = "⌨",
            FontSize = 14,
            Margin = new Thickness(6, 1, 4, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Tag = "nav:keys",
            ToolTip = "",
        };
        _caps.Add(sign);
        sign.ToolTipOpening += (_, _) => sign.ToolTip = MoreKeysText();
        var slot = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        slot.Children.Add(sign);
        return slot;
    }

    /// <summary>What the keyboard sign's tip says.</summary>
    internal string MoreKeysText()
    {
        var lines = new List<string> { Loc.T("navigator.moreKeys") };
        if (CopyCommand != null && PasteCommand != null) lines.Add(Loc.T("navigator.moreKeys.copyPaste"));
        if (RenameCommand != null) lines.Add(Loc.T("navigator.moreKeys.f12"));
        lines.Add(Loc.T("navigator.moreKeys.undo"));
        return string.Join(Environment.NewLine, lines);
    }

    private void BindKeys(UIElement? list)
    {
        if (list == null) return;
        void On(Key key, ModifierKeys mods, Func<ICommand?> command)
            => list.InputBindings.Add(new KeyBinding(new Deferred(command), key, mods));

        On(Key.Insert, ModifierKeys.None, () => AddCommand);
        On(Key.F2, ModifierKeys.None, () => RenameCommand);
        On(Key.F12, ModifierKeys.None, () => RenameCommand);
        On(Key.D, ModifierKeys.Control, () => DuplicateCommand);
        On(Key.Delete, ModifierKeys.None, () => DeleteKeyCommand ?? DeleteCommand);
        On(Key.C, ModifierKeys.Control, () => CopyCommand);
        On(Key.V, ModifierKeys.Control, () => PasteCommand);
    }

    /// <summary>
    /// A key's command, read when the key is pressed: the bar's commands are
    /// bound, and the binding may not have arrived when the list does. Plain
    /// <see cref="ICommand"/> on purpose - the command it hands on to raises
    /// the undo checkpoint itself, and doing it twice made the first Ctrl+Z
    /// appear to do nothing (see the window's shortcuts).
    /// </summary>
    private sealed class Deferred : ICommand
    {
        private readonly Func<ICommand?> _command;
        public Deferred(Func<ICommand?> command) => _command = command;
        public bool CanExecute(object? parameter) => _command()?.CanExecute(parameter) == true;
        public void Execute(object? parameter) => _command()?.Execute(parameter);
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    private sealed class HasValue : IValueConverter
    {
        public static readonly HasValue Instance = new();
        public object Convert(object? value, Type t, object? p, System.Globalization.CultureInfo c)
            => value == null ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? value, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotSupportedException();
    }
}
