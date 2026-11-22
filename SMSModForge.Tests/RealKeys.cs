using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SMSModForge.Tests;

/// <summary>
/// A key pressed the way Windows hands one to WPF (1.10.0): through the input
/// manager, to whatever has the keyboard, down the whole of WPF's own route -
/// the preview, the key itself, the control's own shortcuts, then the window's.
/// <para/>
/// The undo tests used to run the window's Ctrl+Z binding by hand whenever the
/// box with the keyboard said it had nothing of its own to undo - which is how
/// the route was thought to go. It does not: a text box maps Ctrl+Z to Undo
/// itself, and a control that maps a key to a command it cannot run still marks
/// the key as dealt with. Every test passed while the key, really pressed in a
/// box, did nothing at all. So the key is pressed, not modelled.
/// <para/>
/// Ctrl, Shift and Alt are read by WPF from the thread's own key state, which
/// is set here for the length of the press and put back: it is this thread's
/// alone, on the run's own desktop - nobody's keyboard is touched.
/// </summary>
internal static class RealKeys
{
    [DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState(byte[] state);

    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLShift = 0xA0, VkLControl = 0xA2, VkLMenu = 0xA4;

    /// <summary>Press and let go. True when something took the key down.</summary>
    public static bool Press(Visual within, Key key, ModifierKeys held = ModifierKeys.None)
    {
        var source = PresentationSource.FromVisual(within) ?? throw new InvalidOperationException("nothing is shown to press a key in");
        var before = new byte[256];
        GetKeyboardState(before);
        var during = (byte[])before.Clone();
        void Hold(bool on, int vk, int left)
        {
            during[vk] = during[left] = on ? (byte)0x80 : (byte)0;
        }
        Hold((held & ModifierKeys.Control) != 0, VkControl, VkLControl);
        Hold((held & ModifierKeys.Shift) != 0, VkShift, VkLShift);
        Hold((held & ModifierKeys.Alt) != 0, VkMenu, VkLMenu);
        SetKeyboardState(during);
        try
        {
            if (Keyboard.Modifiers != held)
                throw new InvalidOperationException($"the keys held read as {Keyboard.Modifiers}, not {held}: the press would prove nothing");
            var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            InputManager.Current.ProcessInput(down);
            bool taken = down.Handled || LastKeyDownHandled;
            var up = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent };
            InputManager.Current.ProcessInput(up);
            return taken;
        }
        finally { SetKeyboardState(before); }
    }

    /// <summary>
    /// The keyboard off whatever has it and somewhere else in the window that edits nothing: on the heading of
    /// the tab that is open, as after a click on it.
    /// <para/>
    /// Not nowhere (Keyboard.ClearFocus): a window that is up always has the keyboard somewhere in it - WPF
    /// hands it to what is around a control that goes away - and a key pressed with it nowhere is given to
    /// nobody, which no author can bring about. And not on the window itself, which hands it straight back to
    /// the control that had it.
    /// </summary>
    public static void Elsewhere(Window window)
    {
        var tabs = (System.Windows.Controls.TabControl)window.FindName("MainTabs");
        var heading = tabs.SelectedItem as System.Windows.Controls.TabItem
                      ?? tabs.ItemContainerGenerator.ContainerFromIndex(tabs.SelectedIndex) as System.Windows.Controls.TabItem
                      ?? throw new InvalidOperationException("no tab is open to put the keyboard on");
        var was = Keyboard.FocusedElement;
        heading.Focus();
        Keyboard.Focus(heading);
        // Wherever the look's template puts it - the heading itself, or (EX) a part of the heading's own template -
        // as long as it is on something, and on nothing that edits.
        var now = Keyboard.FocusedElement;
        if (now == null || ReferenceEquals(now, was) && was is not null and not System.Windows.Controls.TabItem
            || now is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.ComboBox
            || now is DependencyObject d && !IsUnder(d, heading) && !ReferenceEquals(d, heading))
            throw new InvalidOperationException($"the keyboard stayed on a {now?.GetType().Name ?? "nothing"} ({Where(now as DependencyObject)}; it was on {Where(was as DependencyObject)}): what follows would prove nothing");
    }

    private static string Where(DependencyObject? d)
    {
        var path = new List<string>();
        for (var p = d; p != null && path.Count < 8; p = p is Visual ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            path.Add(p.GetType().Name + (p is FrameworkElement { Name.Length: > 0 } fe ? "#" + fe.Name : ""));
        return path.Count == 0 ? "nothing" : string.Join(" < ", path);
    }

    private static bool IsUnder(DependencyObject d, DependencyObject root)
    {
        for (DependencyObject? p = d; p != null; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (ReferenceEquals(p, root)) return true;
        return false;
    }

    /// <summary>Whether the key itself (after its preview) was taken by somebody - set by <see cref="Watch"/>.</summary>
    private static bool LastKeyDownHandled;

    /// <summary>Have a window say whether a key down was taken: the preview's own event says only whether the
    /// preview was.</summary>
    public static void Watch(Window window)
    {
        window.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, _) => LastKeyDownHandled = false), handledEventsToo: true);
        window.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler((_, e) => LastKeyDownHandled = e.Handled), handledEventsToo: true);
    }
}
