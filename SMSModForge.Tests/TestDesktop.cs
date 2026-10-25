using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SMSModForge.Tests;

/// <summary>
/// The desktop every window a test builds lives on: one of the run's own,
/// which nobody ever switches to, so nothing a test shows can appear on the
/// author's monitors or take their focus (the author, 2026-10-21: tests run
/// while they work on the other monitors).
/// <para/>
/// Off-screen windows were not enough. Showing a window activates it, and
/// activating takes the foreground from whatever the author is in - on their
/// machine Windows' foreground lock timeout is 0, so any process may take it
/// at any moment, and a sampler caught the harness doing exactly that for
/// seconds at a time. Minimizing was no answer either: WPF lays a minimized
/// window out at 160x39 and gives it no keyboard focus, which is what half the
/// window tests are measuring. On a desktop of its own a window lays out at
/// its real size, draws (hardware tier included) and takes keyboard focus
/// inside the test, and the author's foreground never moves - measured
/// against a sampler of their foreground window, 2026-10-21.
/// <para/>
/// A thread is bound to a desktop by its first window, so <see cref="Enter"/>
/// must come first on any thread that will make one. The progress window is
/// the one exception: it is meant to be seen, and stays on the author's
/// desktop.
/// </summary>
internal static class TestDesktop
{
    private static readonly object Gate = new();
    private static IntPtr _desktop;

    /// <summary>The desktop's name; one per test process.</summary>
    public static string Name { get; } = "SMSModForgeTests-" + Environment.ProcessId;

    /// <summary>
    /// Puts the calling thread on the run's own desktop, making it on first
    /// use. Call before the thread makes any window. Returns false where there
    /// is no desktop to make one beside (a service, a CI agent) - nobody is
    /// there to disturb, so the run goes on where it is.
    /// <para/>
    /// Where somebody IS there, failing to make one throws rather than going
    /// on on their desktop. Windows has only so much room for desktops, and the
    /// parallel runner makes one per tester: running out must stop the tester,
    /// never put its windows on the author's monitors.
    /// </summary>
    public static bool Enter()
    {
        lock (Gate)
        {
            if (_desktop == IntPtr.Zero)
                _desktop = CreateDesktop(Name, IntPtr.Zero, IntPtr.Zero, 0, GENERIC_ALL, IntPtr.Zero);
            // Kept open for the whole run: the desktop goes when its last
            // handle does, and the process ending closes this one.
            if (_desktop != IntPtr.Zero && SetThreadDesktop(_desktop)) return true;
            int error = Marshal.GetLastWin32Error();
            if (Environment.UserInteractive && InputName().Length > 0)
                throw new InvalidOperationException(
                    $"Could not put this thread on a desktop of the run's own (Windows error {error}; windows the " +
                    $"thread already has: {WindowsOfThisThread()}), and test windows must never reach the author's " +
                    "screen, so this test process stops here.");
            return false;
        }
    }

    /// <summary>The run's desktop, made here if no window has needed it yet.</summary>
    private static IntPtr Handle
    {
        get
        {
            lock (Gate)
            {
                if (_desktop == IntPtr.Zero)
                    _desktop = CreateDesktop(Name, IntPtr.Zero, IntPtr.Zero, 0, GENERIC_ALL, IntPtr.Zero);
                if (_desktop == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"Could not make the run's own desktop (Windows error {Marshal.GetLastWin32Error()}).");
                return _desktop;
            }
        }
    }

    /// <summary>
    /// Starts a program on the run's own desktop rather than the author's: the
    /// editor itself, when a test needs it as a process of its own. One test
    /// opened it on the author's screen for every run until 2026-10-22, when a
    /// sampler of their foreground caught it taking their focus.
    /// </summary>
    public static Launched Launch(string program, string workingDirectory, params string[] arguments)
    {
        _ = Handle;
        var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = "WinSta0\\" + Name };
        string commandLine = string.Join(" ", new[] { program }.Concat(arguments).Select(Quote));
        if (!CreateProcess(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero,
                           workingDirectory, ref startup, out var info))
            throw new InvalidOperationException($"Could not start {program} (Windows error {Marshal.GetLastWin32Error()}).");
        CloseHandle(info.hThread);
        return new Launched(info.hProcess, info.dwProcessId);
    }

    /// <summary>A program <see cref="Launch"/> started. Its own handle is kept:
    /// .NET will not give the exit code of a process it did not start itself.</summary>
    public sealed class Launched : IDisposable
    {
        private IntPtr _handle;

        internal Launched(IntPtr handle, int id)
        {
            _handle = handle;
            Id = id;
        }

        public int Id { get; }

        public bool WaitForExit(int milliseconds) => WaitForSingleObject(_handle, (uint)milliseconds) == 0;

        public int ExitCode => GetExitCodeProcess(_handle, out uint code) ? unchecked((int)code) : -1;

        public void Kill(bool entireProcessTree)
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(Id);
                p.Kill(entireProcessTree);
            }
            catch (ArgumentException) { }            // already gone
            catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero) CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private static string Quote(string argument)
        => argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0
            ? argument
            : "\"" + argument.Replace("\"", "\\\"") + "\"";

    /// <summary>Whether a process has a visible window on the run's desktop.
    /// Process.MainWindowHandle looks only on the desktop of the thread asking,
    /// which is the author's.</summary>
    public static bool HasWindow(int processId)
    {
        bool found = false;
        EnumDesktopWindows(Handle, (h, _) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == processId && IsWindowVisible(h)) { found = true; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>The window classes the calling thread owns, for saying why it
    /// could not move.</summary>
    private static string WindowsOfThisThread()
    {
        var names = new System.Collections.Generic.List<string>();
        EnumThreadWindows(GetCurrentThreadId(), (h, _) =>
        {
            var name = new StringBuilder(256);
            GetClassName(h, name, name.Capacity);
            names.Add(name.ToString());
            return true;
        }, IntPtr.Zero);
        // Message-only windows are not among a thread's windows to
        // EnumThreadWindows, and COM's are message-only.
        uint me = GetCurrentThreadId();
        for (IntPtr h = FindWindowEx(HWND_MESSAGE, IntPtr.Zero, null, null); h != IntPtr.Zero;
             h = FindWindowEx(HWND_MESSAGE, h, null, null))
        {
            if (GetWindowThreadProcessId(h, out _) != me) continue;
            var name = new StringBuilder(256);
            GetClassName(h, name, name.Capacity);
            names.Add(name + " (message-only)");
        }
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    /// <summary>The name of the desktop the calling thread is on.</summary>
    public static string CurrentName() => NameOf(GetThreadDesktop(GetCurrentThreadId()));

    /// <summary>The name of the desktop the author is looking at.</summary>
    public static string InputName()
    {
        var input = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (input == IntPtr.Zero) return "";
        try { return NameOf(input); }
        finally { CloseDesktop(input); }
    }

    private static string NameOf(IntPtr desktop)
    {
        var buffer = new StringBuilder(256);
        return GetUserObjectInformation(desktop, UOI_NAME, buffer, buffer.Capacity * 2, out _)
            ? buffer.ToString() : "";
    }

    private const uint GENERIC_ALL = 0x10000000;
    private const uint DESKTOP_READOBJECTS = 0x0001;
    private const int UOI_NAME = 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr attributes);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr desktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumDesktopWindows(IntPtr desktop, EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string? application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string? directory,
        ref STARTUPINFO startup, out PROCESS_INFORMATION info);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(int flags, bool inherit, uint access);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int needed);
}
