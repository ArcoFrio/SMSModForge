using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SMSModForge.View.Controls;

/// <summary>
/// Preview parallax follows the mouse over the whole screen (1.10.0, the
/// author: "Could we make the parallax preview follow the mouse across the
/// whole monitor(s) instead of only just inside the preview?").
/// <para/>
/// The game drifts a place by where the cursor is on its screen: the middle
/// of the screen is the place as it was made, an edge is as far as it goes.
/// The preview used to read the cursor's place over its own picture instead,
/// and only while the cursor was over it - so the drift stopped the moment the
/// mouse left the picture, and a small move inside it was a large one in the
/// game. Now the screen the preview is on stands for the game's screen: the
/// cursor's place across that screen is the cursor's place in the game, as
/// when the game fills it. On another monitor the drift goes on past the edge,
/// as the game's does: the game reads the cursor wherever it is, and with a
/// second monitor beside a full-screen game the place drifts further than one
/// screen allows (the author, 2026-11-20: "similar to how the game already
/// behaves").
/// <para/>
/// Asked for each frame the screen shows while Preview parallax is ticked and
/// the preview is on screen (two calls to Windows), and nothing is moved
/// unless the cursor has.
/// </summary>
public sealed partial class PlacePreview
{
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    private const uint MonitorNearest = 2;

    private bool _followsCursor;

    /// <summary>Where the cursor is across the screen: 0 to 1 each way on it, past those on another monitor;
    /// NaN until it has been asked.</summary>
    private double _cursorAcross = double.NaN, _cursorDown = double.NaN;

    /// <summary>Start or stop asking where the cursor is: while Preview parallax is ticked and the preview is on screen.</summary>
    private void SyncParallaxFollow()
    {
        bool on = ParallaxPreview && IsVisible;
        if (on == _followsCursor) return;
        _followsCursor = on;
        if (on)
        {
            CompositionTarget.Rendering += OnParallaxFrame;
        }
        else
        {
            CompositionTarget.Rendering -= OnParallaxFrame;
            _cursorAcross = _cursorDown = double.NaN;
            ResetParallax();
        }
    }

    private void OnParallaxFrame(object? sender, EventArgs e)
    {
        // No cursor of anybody's is read by a test: one hands a place on a screen to FollowCursor itself.
        if (Services.TestMode.Active) return;
        if (PresentationSource.FromVisual(this) is not HwndSource source || !GetCursorPos(out var at)) return;
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(source.Handle, MonitorNearest), ref info)) return;
        FollowCursor(at.X, at.Y, new Rect(info.Monitor.Left, info.Monitor.Top,
                                          Math.Max(1, info.Monitor.Right - info.Monitor.Left), Math.Max(1, info.Monitor.Bottom - info.Monitor.Top)));
    }

    /// <summary>
    /// The cursor at a place on the desktop, and the screen the preview is on (both as Windows counts them):
    /// the place drifts as the game drifts it with the cursor there on its own screen - and, off that screen,
    /// on as far as the cursor goes, as the game's does. Not while the view is being dragged about or a handle
    /// is held: that is the hand working, not looking.
    /// </summary>
    internal void FollowCursor(double x, double y, Rect screen)
    {
        if (!ParallaxPreview || screen.Width <= 0 || screen.Height <= 0) return;
        if (_panning || _activeHandle != GizmoHandle.None) return;
        double across = (x - screen.X) / screen.Width;
        double down = (y - screen.Y) / screen.Height;
        if (across == _cursorAcross && down == _cursorDown) return;
        _cursorAcross = across;
        _cursorDown = down;
        ApplyParallax(new Point(across * CanvasWidth, down * CanvasHeight));
    }

    /// <summary>How far the place is drifted now, as the share of the screen the cursor is from its middle -
    /// for tests, and for what is drawn after the place is built again.</summary>
    internal (double Across, double Down)? Drift
        => ParallaxPreview && !double.IsNaN(_cursorAcross) ? (_cursorAcross - 0.5, _cursorDown - 0.5) : null;
}
