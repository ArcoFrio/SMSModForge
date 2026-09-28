using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SMSModForge.Tests;

/// <summary>
/// A small window that says how far along a long test run is: tests done out
/// of the run, a bar, time gone and time left, the test running now and for
/// how long, and what has failed so far. The taskbar button carries the bar
/// too, turning red at the first failure, so it can be read from under other
/// windows (the author, 2026-09-28).
/// <para/>
/// WinForms, on a thread of its own, and on purpose. The harness has the
/// editor's App on its STA thread, and a WPF window on any other thread would
/// look its styles up in App.xaml and reach brushes that belong to the
/// harness's thread; what that throws would end the run. A WinForms window
/// shares nothing with it.
/// <para/>
/// It comes up on its own only for a run that is going to take a while - at
/// once when the last times say a minute or more, otherwise once a run has
/// gone half a minute - and never unattended. SMSMODFORGE_TEST_PROGRESS=0
/// keeps it away; =1 shows it for every run.
/// </summary>
internal sealed class RunProgressWindow : Form
{
    private static readonly TimeSpan LongRun = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Stuck = TimeSpan.FromMinutes(1);

    private readonly RunProgress _run;
    private readonly Label _count = new();
    private readonly ProgressBar _bar = new();
    private readonly Label _time = new();
    private readonly Label _now = new();
    private readonly Label _failedLabel = new();
    private readonly ListBox _failures = new();
    private readonly CheckBox _onTop = new();
    private readonly System.Windows.Forms.Timer _tick = new();
    private ITaskbarList3? _taskbar;
    private bool _red;

    public static void OfferFor(RunProgress run)
    {
        var wanted = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_PROGRESS");
        if (wanted == "0" || !Environment.UserInteractive) return;
        bool atOnce = wanted == "1" || run.ExpectedSeconds >= LongRun.TotalSeconds;

        var thread = new Thread(() =>
        {
            try
            {
                if (!atOnce && run.Ended.Wait(Patience)) return;
                Application.EnableVisualStyles();
                Application.Run(new RunProgressWindow(run));
            }
            catch
            {
                // No window is not a failed run.
            }
        })
        {
            IsBackground = true,   // never hold the test run open
            Name = "Test run progress",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    internal RunProgressWindow(RunProgress run)
    {
        _run = run;
        SuspendLayout();

        Text = "SMSModForge tests";
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(12);
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;

        _count.Dock = DockStyle.Top;
        _count.Height = 26;
        _count.Font = new Font("Segoe UI", 11f, FontStyle.Bold);

        _bar.Dock = DockStyle.Top;
        _bar.Height = 22;
        _bar.Maximum = 1000;
        _bar.Style = ProgressBarStyle.Continuous;

        _time.Dock = DockStyle.Top;
        _time.Height = 26;
        _time.Padding = new Padding(0, 6, 0, 0);

        _now.Dock = DockStyle.Top;
        _now.Height = 22;
        _now.AutoEllipsis = true;

        _failedLabel.Dock = DockStyle.Top;
        _failedLabel.Height = 26;
        _failedLabel.Padding = new Padding(0, 6, 0, 0);

        _failures.Dock = DockStyle.Fill;
        _failures.IntegralHeight = false;
        _failures.HorizontalScrollbar = true;

        _onTop.Dock = DockStyle.Bottom;
        _onTop.Height = 26;
        _onTop.Text = "Keep on top";
        _onTop.CheckedChanged += (_, _) => TopMost = _onTop.Checked;

        // Docking goes from the last added outwards: the list fills what the
        // rest leave, and the count ends up at the very top.
        Controls.Add(_failures);
        Controls.Add(_onTop);
        Controls.Add(_failedLabel);
        Controls.Add(_now);
        Controls.Add(_time);
        Controls.Add(_bar);
        Controls.Add(_count);

        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 280);
        MinimumSize = new Size(360, 240);
        ResumeLayout(false);
        PerformLayout();

        _tick.Interval = 250;
        _tick.Tick += (_, _) => Tick();
        Tick();
    }

    // Up beside whatever the author is doing, not in front of it.
    protected override bool ShowWithoutActivation => true;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            _taskbar = (ITaskbarList3)new TaskbarList();
            _taskbar.HrInit();
        }
        catch
        {
            _taskbar = null;
        }
        _tick.Start();
    }

    internal string CountText => _count.Text;
    internal string TimeText => _time.Text;
    internal string NowText => _now.Text;
    internal string FailedText => _failedLabel.Text;
    internal int BarValue => _bar.Value;
    internal string[] FailureNames => _failures.Items.Cast<object>().Select(o => o.ToString()!).ToArray();

    internal void Tick()
    {
        var s = _run.Now();
        int percent = (int)Math.Floor(s.Fraction * 100);

        _count.Text = $"{s.Done:N0} of {s.Total:N0} tests" + (s.Skipped > 0 ? $"  ({s.Skipped:N0} skipped)" : "");
        _bar.Value = Math.Clamp((int)Math.Round(s.Fraction * 1000), 0, 1000);
        _time.Text = Clock(s.Elapsed) + " elapsed"
                     + (!s.Finished && s.Left is { } left ? "  ·  " + Remaining(left) : "");
        _now.Text = s.Finished ? "Finished."
                  : s.Running is { } running ? $"Now: {running}  ({Clock(s.RunningFor)})"
                  : "";
        // A test running for a minute is the first sign of one waiting on
        // something that will never come.
        _now.ForeColor = !s.Finished && s.RunningFor >= Stuck ? Color.DarkOrange : SystemColors.ControlText;

        _failedLabel.Text = s.Failed > 0 ? $"{s.Failed:N0} failed:"
                          : s.Finished ? "No failures." : "No failures so far.";
        _failedLabel.ForeColor = s.Failed > 0 ? Color.Firebrick : SystemColors.ControlText;
        while (_failures.Items.Count < s.Failures.Count) _failures.Items.Add(s.Failures[_failures.Items.Count]);

        Text = (s.Finished ? "Done" : percent + "%") + " - SMSModForge tests";

        if (s.Failed > 0 && !_red && _bar.IsHandleCreated)
        {
            SendMessage(_bar.Handle, PBM_SETSTATE, (IntPtr)PBST_ERROR, IntPtr.Zero);
            _red = true;
        }
        if (_taskbar != null && IsHandleCreated)
        {
            try
            {
                _taskbar.SetProgressState(Handle, s.Failed > 0 ? TBPF_ERROR : TBPF_NORMAL);
                _taskbar.SetProgressValue(Handle, (ulong)_bar.Value, 1000);
            }
            catch
            {
                // Before the taskbar has made a button for the window, or none.
            }
        }
        if (s.Finished) _tick.Stop();
    }

    internal static string Clock(TimeSpan t)
        => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    internal static string Remaining(TimeSpan left)
    {
        if (left < TimeSpan.FromMinutes(1)) return "less than a minute left";
        int minutes = (int)Math.Ceiling(left.TotalMinutes);
        return minutes < 60 ? $"about {minutes} min left" : $"about {minutes / 60} h {minutes % 60} min left";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tick.Dispose();
        base.Dispose(disposing);
    }

    // ── The taskbar button's own bar, and the red one ─────────────────────

    private const int PBM_SETSTATE = 0x0410;
    private const int PBST_ERROR = 2;
    private const int TBPF_NORMAL = 0x2;
    private const int TBPF_ERROR = 0x4;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int state);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList { }
}
