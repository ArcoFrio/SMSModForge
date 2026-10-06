using System;
using System.Collections.Generic;
using SMSModForge.Model;

namespace SMSModForge.Services.Audio;

/// <summary>
/// Makes an edited sound: the pieces kept, in their order, then pitch and
/// speed, then echo, then reverb - the same steps for the preview and for the
/// file written when the pack is saved, so what plays in the editor is what
/// the game plays.
/// <para/>
/// Pitch and speed are kept apart, as the author asked for both: speed changes
/// how long the sound lasts and not how high it is (a time stretch, WSOLA),
/// and pitch changes how high it is and not how long it lasts (the same
/// stretch, then played back faster or slower to land on the length it had).
/// </summary>
public static class SfxRenderer
{
    /// <summary>The whole edit applied to one recording.</summary>
    public static AudioData Render(AudioData source, IReadOnlyList<SfxPieceDef>? pieces, SfxEditDef? edit)
    {
        var sound = Cut(source, pieces);
        if (edit == null) return sound;

        double pitch = Math.Pow(2, Math.Clamp(edit.Pitch, -24, 24) / 12.0);
        double speed = Math.Clamp(edit.Speed <= 0 ? 1 : edit.Speed, 0.25, 4);
        if (Math.Abs(pitch - 1) > 1e-6 || Math.Abs(speed - 1) > 1e-6)
        {
            sound = Stretch(sound, pitch / speed);
            sound = Resample(sound, pitch);
        }
        if (edit.Echo is { Amount: > 0 } echo) sound = Echo(sound, echo.Delay, echo.Amount);
        if (edit.Reverb is { Amount: > 0 } reverb) sound = Reverb(sound, reverb.Amount, reverb.Room);
        return Limit(sound);
    }

    /// <summary>How long a stretch of the edited sound lasts for each second
    /// of its pieces - what the strip's cursor is multiplied by to find the
    /// same moment in what plays.</summary>
    public static double TimeScale(SfxEditDef? edit)
        => edit == null ? 1 : 1 / Math.Clamp(edit.Speed <= 0 ? 1 : edit.Speed, 0.25, 4);

    // ── Pieces ───────────────────────────────────────────────────────────

    /// <summary>Longest fade given to a cut edge: long enough that the cut
    /// does not click, too short to hear as a fade.</summary>
    private const double EdgeFade = 0.004;

    /// <summary>
    /// The pieces, one after another. An edge the author cut gets a fade of a
    /// few milliseconds - a cut through the middle of a wave jumps from one
    /// level to another, and a jump is a click. The recording's own start and
    /// end are left as they are.
    /// </summary>
    public static AudioData Cut(AudioData source, IReadOnlyList<SfxPieceDef>? pieces)
    {
        if (pieces == null || pieces.Count == 0) return source;

        int total = 0;
        var spans = new List<(int From, int To)>();
        foreach (var p in pieces)
        {
            int a = source.FrameAt(Math.Min(p.From, p.To)), b = source.FrameAt(Math.Max(p.From, p.To));
            if (b <= a) continue;
            spans.Add((a, b));
            total += b - a;
        }
        var cut = source.Empty(total);
        int at = 0;
        int fadeMax = (int)(EdgeFade * source.SampleRate);
        foreach (var (a, b) in spans)
        {
            int len = b - a;
            int fade = Math.Min(fadeMax, len / 4);
            for (int c = 0; c < source.ChannelCount; c++)
            {
                Array.Copy(source.Channels[c], a, cut.Channels[c], at, len);
                if (fade <= 0) continue;
                for (int i = 0; i < fade; i++)
                {
                    float g = (float)i / fade;
                    if (a > 0) cut.Channels[c][at + i] *= g;
                    if (b < source.Frames) cut.Channels[c][at + len - 1 - i] *= g;
                }
            }
            at += len;
        }
        return cut;
    }

    // ── Speed and pitch ──────────────────────────────────────────────────

    /// <summary>
    /// Make the sound <paramref name="factor"/> times as long without changing
    /// its pitch: WSOLA - short overlapping frames laid out at the new spacing,
    /// each taken from near where it falls in the recording, at the point that
    /// lines up best with what came before, so the waves join rather than beat.
    /// </summary>
    public static AudioData Stretch(AudioData x, double factor)
    {
        if (Math.Abs(factor - 1) < 1e-6 || x.Frames == 0) return x;

        int n = Math.Max(64, (int)(x.SampleRate * 0.03) & ~1);   // 30 ms frames
        int hs = n / 2;                                         // half overlap
        double ha = hs / factor;
        int tolerance = n / 4;
        int overlap = n - hs;

        var mono = Mono(x);
        int outLen = (int)Math.Ceiling(x.Frames * factor);
        var y = x.Empty(outLen + n);
        var weight = new float[outLen + n];
        var window = Hann(n);

        int previous = 0;
        for (int k = 0; ; k++)
        {
            int synth = k * hs;
            if (synth >= outLen) break;

            int pos;
            int nominal = (int)Math.Round(k * ha);
            if (k == 0) pos = 0;
            else
            {
                int natural = previous + hs;
                pos = BestMatch(mono, natural, nominal, tolerance, overlap);
            }

            for (int i = 0; i < n; i++)
            {
                int from = pos + i;
                if (from >= x.Frames) break;
                float w = window[i];
                for (int c = 0; c < x.ChannelCount; c++) y.Channels[c][synth + i] += w * x.Channels[c][from];
                weight[synth + i] += w;
            }
            previous = pos;
        }

        var result = x.Empty(outLen);
        for (int c = 0; c < x.ChannelCount; c++)
            for (int i = 0; i < outLen; i++)
            {
                float w = weight[i];
                result.Channels[c][i] = w > 0.1f ? y.Channels[c][i] / w : y.Channels[c][i];
            }
        return result;
    }

    /// <summary>Where near <paramref name="nominal"/> the recording looks most
    /// like what follows the previous frame at <paramref name="natural"/>.</summary>
    private static int BestMatch(float[] mono, int natural, int nominal, int tolerance, int length)
    {
        int last = mono.Length - length;
        if (last <= 0) return Math.Clamp(nominal, 0, Math.Max(0, mono.Length - 1));
        int lo = Math.Clamp(nominal - tolerance, 0, last);
        int hi = Math.Clamp(nominal + tolerance, 0, last);
        if (natural < 0 || natural > last || hi <= lo) return Math.Clamp(nominal, 0, last);

        double Score(int p)
        {
            double s = 0;
            for (int i = 0; i < length; i += 2) s += mono[natural + i] * mono[p + i];
            return s;
        }

        // Coarse then fine: every fourth candidate, then each one around the
        // best of those.
        int best = lo;
        double bestScore = double.MinValue;
        for (int p = lo; p <= hi; p += 4)
        {
            double s = Score(p);
            if (s > bestScore) { bestScore = s; best = p; }
        }
        int fineLo = Math.Max(lo, best - 3), fineHi = Math.Min(hi, best + 3);
        for (int p = fineLo; p <= fineHi; p++)
        {
            double s = Score(p);
            if (s > bestScore) { bestScore = s; best = p; }
        }
        return best;
    }

    /// <summary>
    /// Play the sound <paramref name="ratio"/> times as fast: higher by that
    /// much, and shorter by it. Cubic between samples; taken through a
    /// low-pass first when it is sped up, or what is too high to keep folds
    /// back down as noise.
    /// </summary>
    public static AudioData Resample(AudioData x, double ratio)
    {
        if (Math.Abs(ratio - 1) < 1e-6 || x.Frames == 0) return x;
        var source = ratio > 1.02 ? LowPass(x, 0.5 / ratio) : x;

        int outLen = Math.Max(1, (int)Math.Floor((x.Frames - 1) / ratio) + 1);
        var y = x.Empty(outLen);
        for (int c = 0; c < x.ChannelCount; c++)
        {
            var s = source.Channels[c];
            var d = y.Channels[c];
            int last = s.Length - 1;
            for (int i = 0; i < outLen; i++)
            {
                double t = i * ratio;
                int i1 = (int)t;
                double f = t - i1;
                float p0 = s[Math.Clamp(i1 - 1, 0, last)], p1 = s[Math.Clamp(i1, 0, last)];
                float p2 = s[Math.Clamp(i1 + 1, 0, last)], p3 = s[Math.Clamp(i1 + 2, 0, last)];
                // Catmull-Rom.
                d[i] = (float)(p1 + 0.5 * f * (p2 - p0 + f * (2 * p0 - 5 * p1 + 4 * p2 - p3 + f * (3 * (p1 - p2) + p3 - p0))));
            }
        }
        return y;
    }

    /// <summary>A windowed-sinc low-pass at <paramref name="cutoff"/> of the
    /// sample rate.</summary>
    private static AudioData LowPass(AudioData x, double cutoff)
    {
        const int taps = 63;
        var h = new double[taps];
        double sum = 0;
        for (int i = 0; i < taps; i++)
        {
            int m = i - taps / 2;
            double sinc = m == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * m) / (Math.PI * m);
            double blackman = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (taps - 1));
            h[i] = sinc * blackman;
            sum += h[i];
        }
        for (int i = 0; i < taps; i++) h[i] /= sum;

        var y = x.Empty(x.Frames);
        for (int c = 0; c < x.ChannelCount; c++)
        {
            var s = x.Channels[c];
            var d = y.Channels[c];
            for (int i = 0; i < s.Length; i++)
            {
                double acc = 0;
                for (int k = 0; k < taps; k++)
                {
                    int j = i + k - taps / 2;
                    if (j >= 0 && j < s.Length) acc += h[k] * s[j];
                }
                d[i] = (float)acc;
            }
        }
        return y;
    }

    // ── Echo ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The sound again after <paramref name="delay"/> seconds, and again, each
    /// time <paramref name="amount"/> as loud as the last - lengthened so the
    /// last repeat is heard to the end rather than cut off.
    /// </summary>
    public static AudioData Echo(AudioData x, double delay, double amount)
    {
        amount = Math.Clamp(amount, 0, 0.9);
        if (amount <= 0) return x;
        int d = Math.Max(1, (int)Math.Round(Math.Clamp(delay, 0.01, 2) * x.SampleRate));
        int repeats = (int)Math.Ceiling(Math.Log(0.001) / Math.Log(amount));
        int tail = (int)Math.Min(4L * x.SampleRate, (long)d * repeats);

        var y = x.Empty(x.Frames + tail);
        float a = (float)amount;
        for (int c = 0; c < x.ChannelCount; c++)
        {
            var s = x.Channels[c];
            var o = y.Channels[c];
            for (int i = 0; i < o.Length; i++)
            {
                float v = i < s.Length ? s[i] : 0;
                if (i >= d) v += a * o[i - d];
                o[i] = v;
            }
        }
        return TrimSilentTail(y, x.Frames);
    }

    // ── Reverb ───────────────────────────────────────────────────────────

    // Freeverb's tuning (Jezar at Dreampoint, public domain), at 44.1 kHz.
    private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    private static readonly int[] AllpassTuning = { 556, 441, 341, 225 };
    private const int StereoSpread = 23;

    /// <summary>
    /// The sound in a room: Freeverb - eight damped combs and four all-passes
    /// per channel, the right one a little apart from the left so a stereo
    /// sound stays wide. <paramref name="amount"/> is how much of the room is
    /// heard over the sound itself, <paramref name="room"/> how big it is.
    /// </summary>
    public static AudioData Reverb(AudioData x, double amount, double room)
    {
        amount = Math.Clamp(amount, 0, 1);
        if (amount <= 0) return x;
        room = Math.Clamp(room, 0, 1);

        int tail = (int)((0.6 + 2.4 * room) * x.SampleRate);
        var y = x.Empty(x.Frames + tail);
        double scale = x.SampleRate / 44100.0;
        float feedback = (float)(room * 0.28 + 0.7);
        const float damp = 0.2f, gain = 0.015f;

        // The room is fed the sound summed, as Freeverb does.
        var input = new float[y.Frames];
        for (int c = 0; c < x.ChannelCount; c++)
            for (int i = 0; i < x.Frames; i++) input[i] += x.Channels[c][i];
        float inScale = gain * (2f / x.ChannelCount);

        for (int c = 0; c < x.ChannelCount; c++)
        {
            int spread = c == 1 ? StereoSpread : 0;
            var combs = new Comb[CombTuning.Length];
            for (int k = 0; k < combs.Length; k++) combs[k] = new Comb((int)((CombTuning[k] + spread) * scale), feedback, damp);
            var passes = new Allpass[AllpassTuning.Length];
            for (int k = 0; k < passes.Length; k++) passes[k] = new Allpass((int)((AllpassTuning[k] + spread) * scale));

            var o = y.Channels[c];
            float wet = (float)amount;
            for (int i = 0; i < o.Length; i++)
            {
                float vin = input[i] * inScale;
                float acc = 0;
                foreach (var comb in combs) acc += comb.Process(vin);
                foreach (var pass in passes) acc = pass.Process(acc);
                float dry = i < x.Frames ? x.Channels[c][i] : 0;
                o[i] = dry + wet * acc;
            }
        }
        return TrimSilentTail(y, x.Frames);
    }

    private sealed class Comb
    {
        private readonly float[] _buffer;
        private int _at;
        private float _store;
        private readonly float _feedback, _damp1, _damp2;

        public Comb(int size, float feedback, float damp)
        {
            _buffer = new float[Math.Max(1, size)];
            _feedback = feedback;
            _damp1 = damp;
            _damp2 = 1 - damp;
        }

        public float Process(float input)
        {
            float output = _buffer[_at];
            _store = output * _damp2 + _store * _damp1;
            _buffer[_at] = input + _store * _feedback;
            if (++_at >= _buffer.Length) _at = 0;
            return output;
        }
    }

    private sealed class Allpass
    {
        private readonly float[] _buffer;
        private int _at;

        public Allpass(int size) => _buffer = new float[Math.Max(1, size)];

        public float Process(float input)
        {
            float buffered = _buffer[_at];
            float output = -input + buffered;
            _buffer[_at] = input + buffered * 0.5f;
            if (++_at >= _buffer.Length) _at = 0;
            return output;
        }
    }

    // ── Finishing ────────────────────────────────────────────────────────

    /// <summary>
    /// Bring the loudest point under full scale, when the effects pushed it
    /// over - all of it, by the same amount, rather than flattening the peaks
    /// into a crackle. A sound that was not too loud is left as it is.
    /// </summary>
    public static AudioData Limit(AudioData x)
    {
        float peak = 0;
        foreach (var ch in x.Channels)
            foreach (var v in ch)
                if (Math.Abs(v) > peak) peak = Math.Abs(v);
        if (peak <= 0.99f) return x;
        float g = 0.99f / peak;
        foreach (var ch in x.Channels)
            for (int i = 0; i < ch.Length; i++) ch[i] *= g;
        return x;
    }

    /// <summary>Drop the end of a tail once nothing can be heard in it, never
    /// shorter than <paramref name="keepAtLeast"/>.</summary>
    private static AudioData TrimSilentTail(AudioData y, int keepAtLeast)
    {
        const float silent = 0.0003f;
        int end = y.Frames;
        while (end > keepAtLeast)
        {
            bool quiet = true;
            for (int c = 0; c < y.ChannelCount && quiet; c++)
                if (Math.Abs(y.Channels[c][end - 1]) > silent) quiet = false;
            if (!quiet) break;
            end--;
        }
        if (end == y.Frames) return y;
        var trimmed = y.Empty(end);
        for (int c = 0; c < y.ChannelCount; c++) Array.Copy(y.Channels[c], trimmed.Channels[c], end);
        return trimmed;
    }

    private static float[] Mono(AudioData x)
    {
        if (x.ChannelCount == 1) return x.Channels[0];
        var m = new float[x.Frames];
        foreach (var ch in x.Channels)
            for (int i = 0; i < m.Length; i++) m[i] += ch[i];
        return m;
    }

    private static float[] Hann(int n)
    {
        var w = new float[n];
        for (int i = 0; i < n; i++) w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n));
        return w;
    }
}
