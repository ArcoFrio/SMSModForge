using System;

namespace SMSModForge.Services.Audio;

/// <summary>
/// What the SFX strip draws of a sound: its channels mixed to one, and the
/// loudest and quietest point of every block of it, so a column of pixels
/// covering ten thousand samples is found in a few steps rather than ten
/// thousand.
/// </summary>
public sealed class WavePeaks
{
    private const int Block = 256;

    private readonly float[] _mix;
    private readonly float[] _blockMin;
    private readonly float[] _blockMax;

    public int SampleRate { get; }
    public int Frames => _mix.Length;
    public double Seconds => (double)_mix.Length / SampleRate;

    public WavePeaks(AudioData data)
    {
        SampleRate = data.SampleRate;
        int frames = data.Frames;
        _mix = new float[frames];
        for (int c = 0; c < data.ChannelCount; c++)
        {
            var ch = data.Channels[c];
            for (int f = 0; f < frames; f++) _mix[f] += ch[f];
        }
        if (data.ChannelCount > 1)
            for (int f = 0; f < frames; f++) _mix[f] /= data.ChannelCount;

        int blocks = (frames + Block - 1) / Block;
        _blockMin = new float[blocks];
        _blockMax = new float[blocks];
        for (int b = 0; b < blocks; b++)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            int end = Math.Min(frames, (b + 1) * Block);
            for (int f = b * Block; f < end; f++)
            {
                float v = _mix[f];
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            _blockMin[b] = lo;
            _blockMax[b] = hi;
        }
    }

    /// <summary>The quietest and loudest point between two times, in
    /// seconds; zero for a stretch with no samples in it.</summary>
    public (float Min, float Max) Range(double fromSeconds, double toSeconds)
    {
        int a = (int)Math.Clamp(Math.Floor(fromSeconds * SampleRate), 0, Frames);
        int b = (int)Math.Clamp(Math.Ceiling(toSeconds * SampleRate), 0, Frames);
        if (b <= a) return a < Frames ? (_mix[a], _mix[a]) : (0, 0);

        float lo = float.MaxValue, hi = float.MinValue;
        int f = a;
        while (f < b)
        {
            if (f % Block == 0 && f + Block <= b)
            {
                int blk = f / Block;
                if (_blockMin[blk] < lo) lo = _blockMin[blk];
                if (_blockMax[blk] > hi) hi = _blockMax[blk];
                f += Block;
            }
            else
            {
                float v = _mix[f];
                if (v < lo) lo = v;
                if (v > hi) hi = v;
                f++;
            }
        }
        return (lo, hi);
    }
}
