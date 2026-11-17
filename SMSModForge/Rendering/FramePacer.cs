using System;
using System.Diagnostics;

namespace SMSModForge.Rendering;

/// <summary>
/// Which of the display's frames a live preview draws on, so that it draws the
/// number a second its preview quality asks for (1.9.1). Ask <see cref="Due"/>
/// on every frame the display offers; it says yes on a steady schedule of
/// <c>1000 / fps</c> ms, taken from a precise clock.
/// <para/>
/// Until 1.9.1 each preview compared <see cref="Environment.TickCount64"/>
/// against the cap. That clock moves in steps of 15 or 16 ms, so a frame
/// 16.7 ms after the last read as 15 and was skipped, and the caps came out as
/// what fits whole steps: 60 a second gave about 40, 30 gave 21 and 20 gave 16
/// in the editor's own window, and on a simulated 60 Hz display 38, 21 and 16
/// (FramePacerTests' control).
/// <para/>
/// A frame may come a little early: within a quarter of the interval, or 4 ms
/// at most, it counts as on time, or a cap equal to the display's own rate
/// would skip every frame that lands a hair before its moment. A preview that
/// fell a whole interval behind (hidden, or busy) starts its schedule again
/// from now rather than drawing a burst to catch up.
/// </summary>
public sealed class FramePacer
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    private readonly Func<double> _nowMs;
    private double _next = double.NaN;

    public FramePacer() : this(() => Stopwatch.GetTimestamp() * TicksToMs) { }

    /// <summary>With a clock of the caller's, in ms - for tests.</summary>
    internal FramePacer(Func<double> nowMs) => _nowMs = nowMs;

    /// <summary>Whether to draw on this frame, at <paramref name="fps"/> frames a second.</summary>
    public bool Due(int fps)
    {
        double now = _nowMs();
        double interval = 1000.0 / Math.Max(1, fps);
        if (double.IsNaN(_next) || now >= _next + interval)
        {
            _next = now + interval;
            return true;
        }
        if (now >= _next - Math.Min(4.0, interval / 4))
        {
            _next += interval;
            return true;
        }
        return false;
    }

    /// <summary>The next frame offered is drawn, and the schedule starts from it.</summary>
    public void Restart() => _next = double.NaN;
}
