using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SMSModForge.Services;

/// <summary>
/// A window's title bar and outline, drawn by Windows itself to match the
/// theme (1.8.0): dark for a dark theme - every dark theme had a white title
/// bar over it - and, for an EX theme, the theme's own bar colour and words
/// with the accent round the window's edge.
/// <para/>
/// Windows' own window attributes (DWM) rather than a title bar drawn by the
/// editor: snapping, the caption buttons, the system menu and resizing stay
/// exactly Windows'. The colours need Windows 11; older Windows refuses those
/// attributes, which is a no-op there, and keeps only the dark mode (Windows
/// 10 20H1 on).
/// </summary>
internal static class NativeWindowLook
{
    internal const int UseImmersiveDarkMode = 20;
    internal const int BorderColour = 34;
    internal const int CaptionColour = 35;
    internal const int CaptionTextColour = 36;

    /// <summary>Windows' own colour for that part, as if never set.</summary>
    internal const int Default = unchecked((int)0xFFFFFFFF);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    public static void Apply(Window window, ThemeDef theme, IReadOnlyDictionary<string, Color> palette)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        Set(hwnd, UseImmersiveDarkMode, theme.IsDark ? 1 : 0);
        Set(hwnd, BorderColour, theme.IsEx ? Ref(palette[ThemeManager.KeyAccent]) : Default);
        Set(hwnd, CaptionColour, theme.IsEx ? Ref(palette[ThemeManager.KeyChrome]) : Default);
        Set(hwnd, CaptionTextColour, theme.IsEx ? Ref(palette[ThemeManager.KeyChromeText]) : Default);
    }

    /// <summary>The attribute set, or Windows' refusal as an HRESULT -
    /// kept for the test that checks the call does anything.</summary>
    internal static int Set(IntPtr hwnd, int attribute, int value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    /// <summary>A colour as Windows takes one: 0x00BBGGRR.</summary>
    internal static int Ref(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
