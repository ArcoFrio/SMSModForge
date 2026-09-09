namespace SMSModForge.Shared
{
    /// <summary>
    /// Which frame of an animation is showing at a given moment.
    /// <para/>
    /// Compiled into both projects from one file, for the usual reason: the
    /// editor's preview and the game have to agree about when a frame changes,
    /// or the preview is showing something the player will never see.
    /// <para/>
    /// Frames carry their OWN durations rather than a shared frame rate,
    /// because a GIF does: every frame in the format has its own delay field,
    /// and half the GIFs in the world use that — a title card held for a second
    /// in front of a fast loop. A constant-rate clock plays those wrong, and it
    /// plays them wrong in a way that looks like a bug in the art.
    /// <para/>
    /// Time is carried as a double of seconds and reduced modulo the loop
    /// BEFORE it is searched, so an animation left running for an hour is as
    /// accurate as one that just started. Accumulating a frame index per update
    /// instead — the obvious way — drifts by whatever the refresh rate does not
    /// divide evenly, and drifts further the longer the scene stays open.
    /// </summary>
    public sealed class AnimationClock
    {
        /// <summary>When each frame ENDS, in seconds from the start. Cumulative
        /// so a lookup is a search rather than a walk.</summary>
        private readonly double[] _ends;

        /// <summary>One loop, in seconds.</summary>
        public double Duration { get; private set; }

        public int FrameCount { get { return _ends.Length; } }

        /// <summary>
        /// Build from per-frame durations in seconds.
        /// <para/>
        /// A non-positive duration is replaced rather than rejected: GIFs in
        /// the wild routinely carry a delay of 0, which every browser treats as
        /// 100ms, and refusing the file would be a worse answer than agreeing
        /// with every other program that opens it.
        /// </summary>
        public AnimationClock(double[] seconds, double fallback)
        {
            if (fallback <= 0.0) fallback = 0.1;
            if (seconds == null || seconds.Length == 0)
            {
                _ends = new double[] { fallback };
                Duration = fallback;
                return;
            }

            _ends = new double[seconds.Length];
            double at = 0.0;
            for (int i = 0; i < seconds.Length; i++)
            {
                double one = seconds[i] > 0.0 ? seconds[i] : fallback;
                at += one;
                _ends[i] = at;
            }
            Duration = at;
        }

        /// <summary>Every frame the same length, from a frame rate.</summary>
        public static AnimationClock AtRate(int frames, double fps)
        {
            if (frames < 1) frames = 1;
            if (fps <= 0.0) fps = 10.0;

            var each = new double[frames];
            for (int i = 0; i < frames; i++) each[i] = 1.0 / fps;
            return new AnimationClock(each, 1.0 / fps);
        }

        /// <summary>
        /// The frame showing at <paramref name="elapsed"/> seconds.
        /// <para/>
        /// Looping folds the time; not looping holds the last frame, which is
        /// what a one-shot animation should leave on screen rather than
        /// vanishing.
        /// </summary>
        public int FrameAt(double elapsed, bool loop)
        {
            if (_ends.Length == 1) return 0;
            if (elapsed <= 0.0) return 0;

            if (loop)
            {
                if (Duration <= 0.0) return 0;
                elapsed = elapsed % Duration;
                if (elapsed < 0.0) elapsed += Duration;

                // The remainder can land a hair BELOW the duration instead of
                // at zero, because neither the frame delays nor the total are
                // exact in binary: 99.0 % 0.3 is 0.2999999999999996, not 0. Left
                // alone that shows the LAST frame at the exact moment the loop
                // restarts - a visible hitch, once per loop, forever. Anything
                // within a rounding error of the end has wrapped.
                if (elapsed >= Duration - Duration * 1e-9) elapsed = 0.0;
            }
            else if (elapsed >= Duration)
            {
                return _ends.Length - 1;
            }

            // Binary search for the first frame that ends after now.
            int lo = 0, hi = _ends.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_ends[mid] <= elapsed) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
}
