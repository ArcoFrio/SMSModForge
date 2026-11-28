using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;

namespace SMSModForge.View.Controls;

/// <summary>
/// What a place's components do, played in the place preview as the game plays them (after 1.10.0, the author: "for
/// the components that show something visually, would we be able to see what they do in real time? Including live
/// preview when changing parameters"): drawn every display frame, from the settings as they are typed.
/// <list type="bullet">
/// <item><b>Movers</b>, carrying the object and everything under it: FloatingSprite (PackComponents), the game's
/// CarInteriorShake (Perlin noise round where it stands, once shakeOnStart) and ConcertLightSwing (a turn between two
/// angles, eased, its speed varied each swing).</item>
/// <item><b>Its own picture's alpha</b>: FadeInSprite and FadeOutSprite - replayed, a pause after each, so they can be
/// watched (the game plays them once, as the object is shown) - BlinkingSprite, and the game's RandomSpriteBlender on
/// each of its children's pictures.</item>
/// <item><b>Which child is on</b>: RandomChildActivator (a new pick every few seconds here, to be seen; the game picks
/// once, as it is shown) and the game's CinemaSpriteChanger (a new one each interval, as the game does).</item>
/// </list>
/// Where the game draws at random, the preview's choices are its own (by the object's place in the tree), the same
/// every time it is built. Each element is moved from where it was made, the mouse's drift (TrackParallax) laid in with
/// it, so the two never undo each other. The handles stay where the object stands.
/// </summary>
public sealed partial class PlacePreview
{
    // ── What moves, fades and picks ──────────────────────────────────────

    /// <summary>Moves what it carries: a change on the canvas at a moment, applied after where it was made.</summary>
    private abstract record Mover
    {
        public abstract Matrix At(double t);
    }

    /// <summary>An offset in its parent's units: the canvas's direction and size of one of them across and up.</summary>
    private abstract record OffsetMover(Vector Across, Vector Up) : Mover
    {
        protected abstract (double X, double Y) Offset(double t);

        public override Matrix At(double t)
        {
            var (x, y) = Offset(t);
            var v = Across * x + Up * y;
            var m = Matrix.Identity;
            m.Translate(v.X, v.Y);
            return m;
        }
    }

    /// <summary>FloatingSprite: a sine up and down, and one side to side over 1.618 times as long.</summary>
    private sealed record FloatMover(double Height, double Period, double Sway, double Start, Vector Across, Vector Up) : OffsetMover(Across, Up)
    {
        protected override (double X, double Y) Offset(double t)
        {
            double rise = Math.Sin(2 * Math.PI * (t / Period + Start)) * Height * 0.5;
            double sway = Sway == 0 ? 0 : Math.Sin(2 * Math.PI * (t / (Period * 1.618) + Start)) * Sway * 0.5;
            return (sway, rise);
        }
    }

    /// <summary>The game's CarInteriorShake: Perlin noise along both axes, (n - 0.5) x 2 x intensity, its clock running at
    /// shakeSpeed (with Perlin off, the game's sines).</summary>
    private sealed record ShakeMover(double Intensity, double Speed, double Scale, bool Perlin, Vector Across, Vector Up) : OffsetMover(Across, Up)
    {
        protected override (double X, double Y) Offset(double t)
        {
            double s = t * Speed;
            if (!Perlin) return (Math.Sin(s) * Intensity, Math.Cos(s * 1.3) * Intensity);
            return ((Noise.Perlin(s * Scale, 0) - 0.5) * 2 * Intensity, (Noise.Perlin(0, s * Scale) - 0.5) * 2 * Intensity);
        }
    }

    /// <summary>The game's ConcertLightSwing: a turn between two angles about the object's own place - ping-pong, eased
    /// with its ease in and out, each swing's speed varied by up to speedVariation.</summary>
    private sealed record SwingMover(double Min, double Max, double Speed, bool Eased, double Variation, double Start, double Authored,
                                     Point Pivot, uint Seed) : Mover
    {
        public override Matrix At(double t)
        {
            // The phase walks at the swing's speed times each cycle's own, which changes as a cycle ends.
            double phase = Start, left = t;
            for (int cycle = 0; cycle < 100000 && left > 0; cycle++)
            {
                double speed = Speed * (Variation > 0 ? 1 + (Unit(Seed, cycle) * 2 - 1) * Variation : 1);
                if (speed <= 0) break;
                double toEnd = (1 - phase) / speed;
                if (left < toEnd) { phase += left * speed; left = 0; }
                else { left -= toEnd; phase = 0; }
            }
            double ping = 1 - Math.Abs(2 * phase - 1);
            double k = Eased ? ping * ping * (3 - 2 * ping) : ping;
            double z = Min + (Max - Min) * k;
            var m = Matrix.Identity;
            // The game sets the turn itself, over the one it was made with; up on the canvas is down: the other way.
            m.RotateAt(-(z - Authored), Pivot.X, Pivot.Y);
            return m;
        }
    }

    /// <summary>Its own picture's alpha at a moment (0..1), in place of the one it was given.</summary>
    private abstract record Fader
    {
        public abstract double At(double t);
    }

    /// <summary>A pause after a fade before it is played again.</summary>
    private const double FadeReplayPause = 1.5;

    private sealed record FadeInFader(double Delay, double Duration, double Target) : Fader
    {
        public override double At(double t)
        {
            double d = Math.Max(0.0001, Duration), at = t % (Delay + d + FadeReplayPause);
            return at < Delay ? 0 : at < Delay + d ? Target * (at - Delay) / d : Target;
        }
    }

    private sealed record FadeOutFader(double Delay, double Duration, double From) : Fader
    {
        public double Cycle => Delay + Math.Max(0.0001, Duration) + FadeReplayPause;

        public override double At(double t)
        {
            double d = Math.Max(0.0001, Duration), at = t % Cycle;
            return at < Delay ? From : at < Delay + d ? From * (1 - (at - Delay) / d) : 0;
        }

        /// <summary>Faded out and switched off: between the fade's end and its replay.</summary>
        public bool Off(double t) => t % Cycle >= Delay + Math.Max(0.0001, Duration);
    }

    /// <summary>BlinkingSprite: its low alpha first, the high one after each interval.</summary>
    private sealed record BlinkFader(double Interval, double Low, double High) : Fader
    {
        public override double At(double t) => ((long)Math.Floor(t / Math.Max(0.01, Interval)) & 1) == 0 ? Low : High;
    }

    /// <summary>The game's RandomSpriteBlender on one of its children: every minDelay..maxDelay seconds a new alpha
    /// (0..1), blended to over blendDuration from where it is.</summary>
    private sealed record BlendFader(double Duration, double MinDelay, double MaxDelay, double From, uint Seed) : Fader
    {
        public override double At(double t)
        {
            // Every child is given a target at the same moments; each its own.
            double at = 0, value = From;
            for (int k = 0; k < 100000; k++)
            {
                double next = at + MinDelay + (MaxDelay - MinDelay) * Unit(Seed ^ 0x5bd1e995u, k);
                double target = Unit(Seed, k);
                double end = at + Math.Max(0.0001, Duration);
                if (t < Math.Min(next, end)) return value + (target - value) * (t - at) / Math.Max(0.0001, Duration);
                if (t < next) return target;
                // The next blend starts from where this one got to.
                value = next >= end ? target : value + (target - value) * (next - at) / Math.Max(0.0001, Duration);
                at = next;
            }
            return value;
        }
    }

    /// <summary>Which of a parent's children is on at a moment.</summary>
    private sealed record ChildPicker(int Count, double Interval, uint Seed)
    {
        public int At(double t) => Count <= 0 ? -1 : (int)(Unit(Seed, (int)Math.Floor(t / Math.Max(0.05, Interval))) * Count) % Count;
    }

    /// <summary>A RandomChildActivator's new pick, here, every this many seconds (the game picks once, as it is shown).</summary>
    private const double RandomChildReplay = 3.0;

    /// <summary>A draw 0..1 of its own for <paramref name="seed"/> and <paramref name="k"/>.</summary>
    private static double Unit(uint seed, int k)
    {
        ulong z = ((ulong)seed << 32) ^ (uint)k ^ 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53));
    }

    private static uint SeedOf(string path)
    {
        uint h = 2166136261;
        foreach (char ch in path) h = (h ^ ch) * 16777619;
        return h;
    }

    // ── Working out each entry's, from its components ───────────────────

    private static ComponentDef? Component(SceneEntry e, string type)
        => (e.Node?.Model.Components ?? e.Npc?.Model.Components)?.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.Ordinal));

    /// <summary>A game component's setting by its name (any case, as the plugin sets it), or the script's own default.</summary>
    private static double Param(ComponentDef c, string name, double fallback)
    {
        foreach (var kv in c.Params)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) && kv.Value != null && kv.Value.Type != JTokenType.Null)
            {
                if (kv.Value.Type is JTokenType.Float or JTokenType.Integer) return (double)kv.Value;
                if (double.TryParse(kv.Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            }
        return fallback;
    }

    private static bool Flag(ComponentDef c, string name, bool fallback)
    {
        foreach (var kv in c.Params)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) && kv.Value != null && kv.Value.Type != JTokenType.Null)
            {
                if (kv.Value.Type == JTokenType.Boolean) return (bool)kv.Value;
                if (bool.TryParse(kv.Value.ToString(), out bool b)) return b;
            }
        return fallback;
    }

    /// <summary>Each entry's movers (its own and every one above it), its own picture's alpha, its parent's blend, and
    /// what decides whether it is on - from the walk's tree order.</summary>
    private void WorkOutAnimations(List<SceneEntry> entries)
    {
        var open = new List<SceneEntry>();
        double ppu = WorldPpu;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            while (open.Count > 0 && open[^1].Depth >= e.Depth) open.RemoveAt(open.Count - 1);
            var parent = open.Count > 0 ? open[^1] : null;
            var p = e.ParentWorld;
            var across = new Vector(p.A * ppu, -p.B * ppu);
            var up = new Vector(p.C * ppu, -p.D * ppu);
            uint seed = SeedOf(e.Path);

            var movers = parent?.Movers ?? Array.Empty<Mover>();
            if (Component(e, PackComponentType.FloatingSprite) is { } fl && fl.FloatPeriod > 0)
                movers = movers.Append(new FloatMover(fl.FloatHeight, fl.FloatPeriod, fl.FloatSway,
                                                      fl.FloatRandomStart ? (seed % 10007) / 10007.0 : fl.FloatStart, across, up)).ToArray();
            if (Component(e, "CarInteriorShake") is { } sh && Flag(sh, "shakeOnStart", false))
                movers = movers.Append(new ShakeMover(Param(sh, "shakeIntensity", 0.1), Param(sh, "shakeSpeed", 10), Param(sh, "perlinScale", 1),
                                                      Flag(sh, "usePerlinNoise", true), across, up)).ToArray();
            if (Component(e, "ConcertLightSwing") is { } sw)
            {
                var w = e.World;
                var pivot = new Point(WorldOriginX + w.Tx * ppu, WorldOriginY - w.Ty * ppu);
                double authored = e.Node?.Model.RotationZ ?? 0;
                movers = movers.Append(new SwingMover(Param(sw, "minAngle", -45), Param(sw, "maxAngle", 45), Param(sw, "swingSpeed", 1),
                                                      Flag(sw, "useEasing", true), Param(sw, "speedVariation", 0.2),
                                                      Flag(sw, "randomizeStartPhase", true) ? Unit(seed, -1) : 0, authored, pivot, seed)).ToArray();
            }
            e.Movers = movers.Length > 0 ? movers : null;

            // Its own picture.
            Fader? fader = null;
            if (Component(e, PackComponentType.FadeInSprite) is { } fi) fader = new FadeInFader(fi.StartDelay, fi.FadeDuration, fi.TargetAlpha);
            else if (Component(e, PackComponentType.FadeOutSprite) is { } fo)
                fader = new FadeOutFader(fo.StartDelay, fo.Duration, e.Node != null ? Math.Clamp(e.Node.Model.StartAlpha, 0, 1) : 1);
            else if (Component(e, PackComponentType.BlinkingSprite) is { } bl) fader = new BlinkFader(bl.BlinkInterval, bl.MinAlpha, bl.MaxAlpha);
            e.Fader = fader;

            // Its parent's blender reads its picture's alpha too.
            e.Blend = parent != null && Component(parent, "RandomSpriteBlender") is { } rb
                ? new BlendFader(Param(rb, "blendDuration", 1), Param(rb, "minDelay", 0.5), Param(rb, "maxDelay", 2),
                                 e.Node != null ? Math.Clamp(e.Node.Model.StartAlpha, 0, 1) : 1, seed)
                : null;

            // Whether it is on: every picker above it, and a fade-out that switches it off.
            var gates = parent?.Gates ?? Array.Empty<Func<double, bool>>();
            if (parent != null && parent.Pickers is { } pick)
            {
                int index = parent.ChildIndex++;
                gates = gates.Append(t => pick.At(t) == index).ToArray();
            }
            if (fader is FadeOutFader fade && Component(e, PackComponentType.FadeOutSprite)!.DeactivateOnComplete)
                gates = gates.Append(t => !fade.Off(t)).ToArray();
            e.Gates = gates.Length > 0 ? gates : null;

            // A picker over its own children, counted from the walk (its direct children follow it, one level down).
            int children = 0;
            for (int j = i + 1; j < entries.Count && entries[j].Depth > e.Depth; j++)
                if (entries[j].Depth == e.Depth + 1) children++;
            e.Pickers = Component(e, PackComponentType.RandomChildActivator) is not null ? new ChildPicker(children, RandomChildReplay, seed)
                      : Component(e, "CinemaSpriteChanger") is { } cs ? new ChildPicker(children, Param(cs, "activationInterval", 1), seed)
                      : null;
            e.ChildIndex = 0;
            open.Add(e);
        }
    }

    // ── Playing them ─────────────────────────────────────────────────────

    /// <summary>An element drawn for an entry: where it was made, its drift, its entry, and its alpha before any picture
    /// alpha of the entry's own (what it would be at an alpha of 1).</summary>
    private readonly List<(UIElement Element, Matrix Base, double Gain, SceneEntry Entry, double Ghost)> _animTargets = new();
    private readonly System.Diagnostics.Stopwatch _animClock = System.Diagnostics.Stopwatch.StartNew();
    private bool _animClockOn;

    /// <summary>How many elements are played now - for tests.</summary>
    internal int AnimatedElements => _animTargets.Count;

    /// <summary>A test's own moment for what the components do, in seconds.</summary>
    internal Func<double>? AnimClockForTests { get; set; }

    /// <summary>Every element put where it is at <paramref name="t"/> - what each display frame does - for tests.</summary>
    internal void AnimateAtForTests(double t) => AnimateTo(t);

    /// <summary>The picture drawn for one of the place's objects, by its name, and the canvas they are drawn on - for tests.</summary>
    internal System.Windows.Controls.Image? ObjectImageFor(string name) => _objectImages.FirstOrDefault(kv => kv.Key.Name == name).Value;
    internal System.Windows.Controls.Canvas SceneCanvas => _canvas;

    private static bool Animated(SceneEntry? e) => e != null && (e.Movers != null || e.Fader != null || e.Blend != null || e.Gates != null);

    /// <summary>An element drawn for an entry the components move, fade or switch is played with it.</summary>
    private void TrackAnimated(UIElement element, Matrix baseM, double gain, SceneEntry? entry)
    {
        if (!Animated(entry)) return;
        double startAlpha = entry!.Node != null ? Math.Clamp(entry.Node.Model.StartAlpha, 0.0, 1.0) : 1.0;
        double ghost = entry.Fader != null || entry.Blend != null ? (startAlpha > 1e-3 ? element.Opacity / startAlpha : 1.0) : element.Opacity;
        _animTargets.Add((element, baseM, gain, entry, ghost));
    }

    /// <summary>Played only while something is and the preview is on screen.</summary>
    private void SyncAnimClock()
    {
        bool want = _animTargets.Count > 0 && IsVisible;
        if (want == _animClockOn) return;
        if (want) CompositionTarget.Rendering += OnAnimFrame;
        else CompositionTarget.Rendering -= OnAnimFrame;
        _animClockOn = want;
        if (want) AnimateTo(AnimNow);
    }

    private double AnimNow => AnimClockForTests?.Invoke() ?? _animClock.Elapsed.TotalSeconds;

    private void OnAnimFrame(object? sender, EventArgs e) => AnimateTo(AnimNow);

    /// <summary>Every played element as it is <paramref name="t"/> seconds in, drifted with the mouse as well.</summary>
    private void AnimateTo(double t)
    {
        var drift = Drift;
        foreach (var (element, baseM, gain, entry, ghost) in _animTargets)
        {
            var m = baseM;
            if (entry.Movers is { } movers)
                // Its own first, then each above it: a parent's motion carries its child's.
                for (int i = movers.Length - 1; i >= 0; i--) m = m * movers[i].At(t);
            if (drift is { } d && Math.Abs(gain) > 1e-6) m.Translate(gain * d.Across, gain * d.Down * 0.5);
            element.RenderTransform = new MatrixTransform(m);

            double alpha = entry.Fader != null || entry.Blend != null
                ? ghost * (entry.Fader?.At(t) ?? (entry.Node != null ? Math.Clamp(entry.Node.Model.StartAlpha, 0.0, 1.0) : 1.0)) * (entry.Blend?.At(t) ?? 1.0)
                : ghost;
            bool on = entry.Gates == null || entry.Gates.All(g => g(t));
            // Off as the game has it: not drawn when it shows what the game shows, else faint, as an object that starts off.
            if (!on) alpha = AsTheGameShows || !DimInactive ? 0 : alpha * 0.35;
            element.Opacity = alpha;
            var shown = alpha > 0 ? Visibility.Visible : Visibility.Hidden;
            if (element.Visibility != shown) element.Visibility = shown;
        }
    }

    // ── Noise ────────────────────────────────────────────────────────────

    /// <summary>Perlin's improved noise in two dimensions, put in 0..1 as the game's Mathf.PerlinNoise is.</summary>
    private static class Noise
    {
        private static readonly int[] P = MakeP();

        private static int[] MakeP()
        {
            int[] perm =
            {
                151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,
                247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,
                74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,
                65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,
                52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,
                119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,
                218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,
                184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180,
            };
            var p = new int[512];
            for (int i = 0; i < 512; i++) p[i] = perm[i & 255];
            return p;
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        private static double Grad(int hash, double x, double y)
        {
            int h = hash & 7;
            double u = h < 4 ? x : y, v = h < 4 ? y : x;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        public static double Perlin(double x, double y)
        {
            int xi = (int)Math.Floor(x) & 255, yi = (int)Math.Floor(y) & 255;
            double xf = x - Math.Floor(x), yf = y - Math.Floor(y);
            double u = Fade(xf), v = Fade(yf);
            int aa = P[P[xi] + yi], ab = P[P[xi] + yi + 1], ba = P[P[xi + 1] + yi], bb = P[P[xi + 1] + yi + 1];
            double x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
            double x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
            return Math.Clamp((Lerp(x1, x2, v) + 1) / 2, 0, 1);
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
