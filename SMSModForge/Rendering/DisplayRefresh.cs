using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SMSModForge.Rendering;

/// <summary>
/// How many frames a second the display shows - what every preview draws at
/// most (1.9.1, the author: "we'd want them all to capped at display refresh
/// rate, just reducing how much of a load it puts on the pc in general").
/// <see cref="RateOf"/>: the monitor a preview's window is on, since a PC's
/// monitors can run at different rates (the author's: 165 Hz and 100 Hz) and
/// the primary one need not be the one the editor is on. Each is read again
/// every few seconds (a window moved, or a display switched to another rate,
/// is picked up); the primary display's rate when Windows will not say for
/// the window's, and 60 when it will not say at all.
/// </summary>
internal static class DisplayRefresh
{
    private const int EnumCurrentSettings = -1;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly object Gate = new();
    private static int _rate = 60;
    private static long _readAt = long.MinValue;
    private static IntPtr _monitor;
    private static int _monitorRate;
    private static long _monitorReadAt = long.MinValue;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Box { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Box rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    /// <summary>The primary display's refresh rate, frames a second.</summary>
    public static int Rate
    {
        get
        {
            long now = Stopwatch.GetTimestamp();
            lock (Gate)
            {
                if (now - _readAt < Stopwatch.Frequency * 5) return _rate;
                _readAt = now;
                if (Read(null) is int rate) _rate = rate;
                return _rate;
            }
        }
    }

    /// <summary>The refresh rate of the monitor <paramref name="visual"/>'s
    /// window is on, frames a second (the primary display's while it is in no
    /// window). Call it on the visual's thread.</summary>
    /// <summary>A display's rate for a test to draw at, whatever the test's own monitor runs at (the author's
    /// main one is 165 Hz, the suite's desktop is wherever it falls); null outside tests.</summary>
    internal static int? RateForTests { get; set; }

    public static int RateOf(Visual? visual)
    {
        if (RateForTests is int forTests) return forTests;
        IntPtr hwnd = visual != null && PresentationSource.FromVisual(visual) is HwndSource source ? source.Handle : IntPtr.Zero;
        IntPtr monitor = hwnd != IntPtr.Zero ? MonitorFromWindow(hwnd, MonitorDefaultToNearest) : IntPtr.Zero;
        if (monitor == IntPtr.Zero) return Rate;
        long now = Stopwatch.GetTimestamp();
        lock (Gate)
        {
            if (monitor == _monitor && now - _monitorReadAt < Stopwatch.Frequency * 5) return _monitorRate;
        }
        int? read = null;
        try
        {
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info)) read = Read(info.szDevice);
        }
        catch (Exception) { /* the primary's below */ }
        int rate = read ?? Rate;
        lock (Gate)
        {
            _monitor = monitor; _monitorRate = rate; _monitorReadAt = now;
        }
        return rate;
    }

    /// <summary>A display's current rate (null for the primary), or null when Windows will not say.</summary>
    private static int? Read(string? device)
    {
        try
        {
            var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            if (EnumDisplaySettings(device, EnumCurrentSettings, ref mode) && mode.dmDisplayFrequency > 1)
                return mode.dmDisplayFrequency;
        }
        catch (Exception) { /* not known */ }
        return null;
    }
}
