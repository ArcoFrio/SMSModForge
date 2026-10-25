// Windows plumbing for Tools\Game\*.ps1, compiled by them with Add-Type: start the game without it ever
// taking the author's focus, put its window on their leftmost monitor, minimized, and mute or unmute the
// game's own audio session - Windows' per-app volume mixer, not the game's settings and not the system.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class GameNative
{
    // ── Starting it ─────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit,
        uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    private const int STARTF_USESHOWWINDOW = 0x1;
    public const short SW_SHOWMINNOACTIVE = 7;

    /// <summary>Starts a program asked to show its first window minimized and not activated - the one way a
    /// window can be told, before it exists, not to take the foreground.</summary>
    public static int StartMinimized(string exe, string arguments, string folder)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), dwFlags = STARTF_USESHOWWINDOW, wShowWindow = SW_SHOWMINNOACTIVE };
        var cmd = new StringBuilder("\"" + exe + "\" " + arguments);
        PROCESS_INFORMATION pi;
        if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, folder, ref si, out pi))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return pi.dwProcessId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr attributes);

    /// <summary>
    /// Starts a program on a desktop of its own, which nobody switches to - so none of its windows can
    /// appear on the author's monitors or take their focus, whatever it does with them. Unity activates its
    /// window while it starts, and on the author's machine any activation takes the foreground (their
    /// foreground lock timeout is 0): starting it minimized was measured taking their focus 5 s in.
    /// The desktop lives as long as the program's threads are on it.
    /// </summary>
    public static int StartOnOwnDesktop(string exe, string arguments, string folder, string desktop)
    {
        IntPtr handle = CreateDesktop(desktop, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000 /* GENERIC_ALL */, IntPtr.Zero);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), lpDesktop = @"WinSta0\" + desktop };
        var cmd = new StringBuilder("\"" + exe + "\" " + arguments);
        PROCESS_INFORMATION pi;
        if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, folder, ref si, out pi))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return pi.dwProcessId;
    }

    // ── Its window ──────────────────────────────────────────────────────

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);

    /// <summary>The program's top-level window - Unity's, by its class - or zero while it has none.</summary>
    public static IntPtr MainWindowOf(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            var cls = new StringBuilder(64); GetClassName(h, cls, 64);
            if (cls.ToString() == "UnityWndClass") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    /// <summary>Moves a window to (x, y) and minimizes it, never activating it.</summary>
    public static void ParkMinimized(IntPtr window, int x, int y)
    {
        SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        if (!IsIconic(window)) ShowWindow(window, SW_SHOWMINNOACTIVE);
    }

    public static bool IsMinimized(IntPtr window) { return IsIconic(window); }

    /// <summary>Which process owns the window the author is in.</summary>
    public static int ForegroundProcess()
    {
        uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p);
        return (int)p;
    }

    // ── Its sound ───────────────────────────────────────────────────────

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object face);
    }

    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr control);
        int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr volume);
        int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int count);
        int GetSession(int index, out IAudioSessionControl2 session);
    }

    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        int GetGroupingParam(out Guid param);
        int SetGroupingParam(ref Guid param, ref Guid context);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetProcessId(out uint pid);
    }

    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        int SetMasterVolume(float level, ref Guid context);
        int GetMasterVolume(out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out int count);
        int SetMasterVolumeLevel(float db, ref Guid context);
        int SetMasterVolumeLevelScalar(float level, ref Guid context);
        int GetMasterVolumeLevel(out float db);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(int channel, float db, ref Guid context);
        int SetChannelVolumeLevelScalar(int channel, float level, ref Guid context);
        int GetChannelVolumeLevel(int channel, out float db);
        int GetChannelVolumeLevelScalar(int channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static IMMDevice Speakers()
    {
        var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice device;
        Marshal.ThrowExceptionForHR(devices.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device));
        return device;
    }

    /// <summary>Mutes or unmutes every audio session the process has on the default output. Returns how
    /// many sessions it found: zero until the game has opened its sound, so callers try again.</summary>
    public static int SetProcessMute(int pid, bool mute)
    {
        var iid = typeof(IAudioSessionManager2).GUID;
        object o;
        Marshal.ThrowExceptionForHR(Speakers().Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o));
        IAudioSessionEnumerator sessions;
        Marshal.ThrowExceptionForHR(((IAudioSessionManager2)o).GetSessionEnumerator(out sessions));
        int count, found = 0;
        sessions.GetCount(out count);
        for (int i = 0; i < count; i++)
        {
            IAudioSessionControl2 session;
            if (sessions.GetSession(i, out session) != 0 || session == null) continue;
            uint owner;
            session.GetProcessId(out owner);
            if (owner != pid) continue;
            var none = Guid.Empty;
            ((ISimpleAudioVolume)session).SetMute(mute, ref none);
            found++;
        }
        return found;
    }

    /// <summary>Whether the process's sessions are muted: null when it has none yet.</summary>
    public static bool? IsProcessMuted(int pid)
    {
        var iid = typeof(IAudioSessionManager2).GUID;
        object o;
        Marshal.ThrowExceptionForHR(Speakers().Activate(ref iid, 23, IntPtr.Zero, out o));
        IAudioSessionEnumerator sessions;
        Marshal.ThrowExceptionForHR(((IAudioSessionManager2)o).GetSessionEnumerator(out sessions));
        int count;
        sessions.GetCount(out count);
        bool? all = null;
        for (int i = 0; i < count; i++)
        {
            IAudioSessionControl2 session;
            if (sessions.GetSession(i, out session) != 0 || session == null) continue;
            uint owner;
            session.GetProcessId(out owner);
            if (owner != pid) continue;
            bool muted;
            ((ISimpleAudioVolume)session).GetMute(out muted);
            all = (all ?? true) && muted;
        }
        return all;
    }

    /// <summary>Windows' own output: muted, and its volume from 0 to 1. The author often leaves it muted,
    /// which a check that listens must say rather than read as silence from the game.</summary>
    public static string SpeakersState()
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        object o;
        Marshal.ThrowExceptionForHR(Speakers().Activate(ref iid, 23, IntPtr.Zero, out o));
        var volume = (IAudioEndpointVolume)o;
        bool muted; float level;
        volume.GetMute(out muted);
        volume.GetMasterVolumeLevelScalar(out level);
        return (muted ? "muted" : "on") + " " + Math.Round(level * 100) + "%";
    }
}
