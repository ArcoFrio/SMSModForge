using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SMSModForge.Services;

/// <summary>
/// Windows' own way of saying "this is done, come back": the taskbar button
/// flashes until the window is brought to the front. For a long run that ends
/// while the author is doing something else - the Translate window's, which
/// can take an hour (the author, 2026-09-28).
/// <para/>
/// Nothing under the test harness: nobody is looking, and the flashing would
/// be on the screen of whoever ran the tests.
/// </summary>
public static class WindowAttention
{
    /// <summary>
    /// Flash <paramref name="window"/>, and the window its taskbar button
    /// belongs to - a dialog owned by the main window has no button of its
    /// own - until the author brings it forward. Nothing when it is in front
    /// already: they are watching.
    /// </summary>
    public static void Flash(Window window)
    {
        if (TestMode.Active || window.IsActive) return;
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        Flash(hwnd);
        IntPtr root = GetAncestor(hwnd, GA_ROOTOWNER);
        if (root != IntPtr.Zero && root != hwnd) Flash(root);
    }

    /// <summary>Flash one window's caption and taskbar button until it comes
    /// to the front.</summary>
    internal static void Flash(IntPtr hwnd)
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount = 0,
            dwTimeout = 0,
        };
        FlashWindowEx(ref info);
    }

    private const uint FLASHW_ALL = 0x3;         // caption and taskbar button
    private const uint FLASHW_TIMERNOFG = 0xC;   // until the window comes to the front
    private const uint GA_ROOTOWNER = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
}
