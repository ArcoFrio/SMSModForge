using System;
using System.Threading.Tasks;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// A bust drawn over a <see cref="PreviewBackdrop"/> as the game draws it
/// there (1.9.1): in a frame that is the game's own screen around the bust
/// (960x540, the bust 512 pixels tall), in linear light from end to end, as
/// the game blends - the body with its fake GI (<see cref="FakeGi"/>), the
/// face over it, the intro's silhouette over that (the body's material, so it
/// takes the fake GI too, in black), what the room's lights add, and the room
/// behind. Breathing moves the bust, not the room, a whole screen pixel at a
/// time (the main camera's Pixel Perfect Camera).
/// <para/>
/// Buffers are premultiplied linear RGBA floats, rows top-down, over the frame.
/// </summary>
internal static class BackdropRender
{
    public const int FrameSize = JiggleShader.RenderSize;

    /// <summary>How many threads a pass uses at most.</summary>
    internal static int Threads = Environment.ProcessorCount;
    private static ParallelOptions Options => new() { MaxDegreeOfParallelism = Math.Max(1, Threads) };

    /// <summary>
    /// The body, at the frame whose bottom-left screen pixel is (<paramref name="left"/>,
    /// <paramref name="frameBottom"/>), the bust's art rect starting at
    /// <paramref name="bustBottom"/>. With <paramref name="gi"/>, its fake GI;
    /// with <paramref name="silhouette"/> above 0, the intro's black silhouette
    /// into <paramref name="silhouetteOut"/> as well. <paramref name="bodyShows"/>
    /// false leaves the body out (the intro's first second).
    /// <para/>
    /// <paramref name="zoom"/>: how many of the game's screen pixels a frame
    /// pixel is, each way (<see cref="JiggleShader.GameZoom"/>). At 1 the program
    /// runs once a frame pixel, at its centre - the game on a 960x540 screen,
    /// which the in-game suite measures; at 2 it runs at the four pixels the
    /// game draws there on a 1920x1080 screen or larger, each at its own centre
    /// (its mask, displacement, art and fake GI, its screen position), and the
    /// frame pixel is their average, premultiplied, in linear light.
    /// </summary>
    public static void Body(byte[] baseTex, byte[] maskTex, JiggleParams p, (float r, float g, float b, float a) tint, float time,
                            FakeGi? gi, FakeGi.Art? art, int left, int frameBottom, int bustBottom,
                            float[] body, bool bodyShows, float[]? silhouetteOut, float silhouette, bool exactBlur = false, float[]? decode = null,
                            int zoom = 1)
    {
        const int S = JiggleShader.Size, N = FrameSize, stride = S * 4;
        if (zoom < 1) throw new ArgumentOutOfRangeException(nameof(zoom));
        int G = N * zoom;                      // the game's pixels across the frame
        // Each of them at its centre: u across the art, v up it (rows top-down).
        var u = new float[G];
        for (int x = 0; x < G; x++) u[x] = (x + 0.5f) / G;
        var v = new float[G];
        for (int gy = 0; gy < G; gy++)
        {
            int y = gy / zoom, sub = gy % zoom;
            v[gy] = (frameBottom + (N - 1 - y) + (zoom - 1 - sub + 0.5f) / zoom - bustBottom) / N;
        }
        var grid = new JiggleShader.Grid(p, time, u, v);
        var lin = decode ?? FakeGi.SrgbToLinear;
        bool white = tint.r == 1f && tint.g == 1f && tint.b == 1f;
        static float Lin(float c) => (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        float tr = white ? 1f : Lin(tint.r), tg = white ? 1f : Lin(tint.g), tb = white ? 1f : Lin(tint.b), ta = tint.a;
        bool withSilhouette = silhouetteOut != null && silhouette > 0f;
        var spans = Spans(baseTex, Reach(p));
        float share = 1f / (zoom * zoom);
        art?.Keep(zoom);

        Parallel.For(0, N, Options, y =>
        {
            int row = y * N * 4;
            // Above or below the bust's quad nothing of it is drawn; beside the
            // art, where no move can bring any of it, it is clear too.
            int from = N, to = -1;
            for (int sub = 0; sub < zoom; sub++)
            {
                float vy = v[y * zoom + sub];
                if (vy >= 0f && vy < 1f)
                {
                    int ty = Math.Clamp((int)MathF.Floor(vy * S), 0, S - 1);
                    from = Math.Min(from, spans[ty * 2] * 2); to = Math.Max(to, spans[ty * 2 + 1] * 2 + 1);
                }
            }
            if (to < from)
            {
                Array.Clear(body, row, N * 4);
                if (withSilhouette) Array.Clear(silhouetteOut!, row, N * 4);
                return;
            }
            Array.Clear(body, row, from * 4);
            Array.Clear(body, row + (to + 1) * 4, (N - 1 - to) * 4);
            if (withSilhouette)
            {
                Array.Clear(silhouetteOut!, row, from * 4);
                Array.Clear(silhouetteOut!, row + (to + 1) * 4, (N - 1 - to) * 4);
            }
            int sy = frameBottom + (N - 1 - y);
            for (int x = from; x <= to; x++)
            {
                int o = row + x * 4;
                float br = 0f, bg = 0f, bb = 0f, ba = 0f, hr = 0f, hg = 0f, hb = 0f, ha = 0f;
                for (int sub = 0; sub < zoom; sub++)
                {
                    int gy = y * zoom + sub;
                    float vy = v[gy];
                    // Off the bust's quad (its top or bottom row, at a zoom): nothing drawn.
                    if (!(vy >= 0f && vy < 1f)) continue;
                    // The game pixel's centre on the screen (from the bottom left).
                    float syc = sy + (zoom - 1 - sub + 0.5f) / zoom;
                    // Its row of the bust drawn at this zoom, from the top, for the gather kept.
                    int keptRow = G - 1 - ((sy - bustBottom) * zoom + (zoom - 1 - sub));
                    for (int subX = 0; subX < zoom; subX++)
                    {
                        int gx = x * zoom + subX;
                        float ux = u[gx];
                        int mx = Math.Clamp((int)MathF.Floor(ux * S), 0, S - 1);
                        int my = Math.Clamp((int)MathF.Floor(vy * S), 0, S - 1);
                        int mi = (S - 1 - my) * stride + mx * 4;
                        var (du, dv) = grid.Offset(gx, gy, maskTex[mi + 2] / 255f, maskTex[mi + 1] / 255f, maskTex[mi] / 255f, maskTex[mi + 3] / 255f);
                        float uu = ux + du, vv = vy + dv;
                        int tx = Math.Clamp((int)MathF.Floor(uu * S), 0, S - 1);
                        int ty = Math.Clamp((int)MathF.Floor(vv * S), 0, S - 1);
                        int ti = (S - 1 - ty) * stride + tx * 4;
                        byte aByte = baseTex[ti + 3];
                        float a = aByte / 255f * ta;
                        bool canLight = gi != null && art != null && a > 0.01f && gi.CanLight(art, tx, ty, aByte);
                        float sxc = left + x + (subX + 0.5f) / zoom;

                        // The gather, kept for a pixel of the bust that has not moved (its
                        // uv is the same whatever the breathing), worked out otherwise.
                        float rim = 0f, gdx = 0f, gdy = 0f;
                        if (canLight && bodyShows)
                        {
                            int bi = keptRow >= 0 && keptRow < G ? keptRow * G + gx : -1;
                            if (bi >= 0 && du == 0f && dv == 0f && art!.StillAlpha[bi] == a) { rim = art.StillRim[bi]; gdx = art.StillDx[bi]; gdy = art.StillDy[bi]; }
                            else
                            {
                                (rim, gdx, gdy) = gi!.Gather(art!, uu, vv, a);
                                if (bi >= 0 && du == 0f && dv == 0f) { art!.StillRim[bi] = rim; art.StillDx[bi] = gdx; art.StillDy[bi] = gdy; art.StillAlpha[bi] = a; }
                            }
                        }

                        if (bodyShows && a > 0f)
                        {
                            float r = lin[baseTex[ti + 2]] * tr, g = lin[baseTex[ti + 1]] * tg, b = lin[baseTex[ti]] * tb;
                            if (canLight) gi!.Light(rim, gdx, gdy, sxc, syc, ref r, ref g, ref b, a, exactBlur);
                            br += MathF.Max(r, 0f) * a; bg += MathF.Max(g, 0f) * a; bb += MathF.Max(b, 0f) * a; ba += a;
                        }

                        if (withSilhouette)
                        {
                            float sa = a * silhouette;
                            if (sa > 0f)
                            {
                                float r = 0f, g = 0f, b = 0f;
                                if (canLight) gi!.Apply(art!, uu, vv, sxc, syc, ref r, ref g, ref b, sa, exactBlur);
                                hr += MathF.Max(r, 0f) * sa; hg += MathF.Max(g, 0f) * sa; hb += MathF.Max(b, 0f) * sa; ha += sa;
                            }
                        }
                    }
                }
                body[o] = br * share; body[o + 1] = bg * share; body[o + 2] = bb * share; body[o + 3] = ba * share;
                if (withSilhouette)
                {
                    silhouetteOut![o] = hr * share; silhouetteOut[o + 1] = hg * share; silhouetteOut[o + 2] = hb * share; silhouetteOut[o + 3] = ha * share;
                }
            }
        });
    }

    /// <summary>How far, in texels, a jiggle with these settings can move a read
    /// (rounded up, and one more for where in its texel the pixel is): the wave
    /// and the bounce at most Strength each, the noise at most 0.65 of Noise
    /// Strength across (its four octaves less a half, and the sway's 0.15) and
    /// 0.5 of it up and down; every mask channel and its alpha at most 1.</summary>
    internal static int Reach(JiggleParams p)
        => (int)MathF.Ceiling((MathF.Abs(p.Strength) + 0.65f * MathF.Abs(p.NoiseStrength)) * JiggleShader.Size) + 1;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], Tuple<int, int[]>> _spans = new();

    /// <summary>For each texel row of the art (from the bottom), the first and
    /// last texel column any read within <paramref name="reach"/> texels of it
    /// can find something drawn at (first above last: nothing).</summary>
    internal static int[] Spans(byte[] art, int reach)
    {
        if (_spans.TryGetValue(art, out var kept) && kept.Item1 == reach) return kept.Item2;
        const int S = JiggleShader.Size;
        var rowMin = new int[S]; var rowMax = new int[S];
        for (int ty = 0; ty < S; ty++)
        {
            int mn = S, mx = -1, r = (S - 1 - ty) * S * 4;
            for (int tx = 0; tx < S; tx++)
                if (art[r + tx * 4 + 3] != 0) { if (tx < mn) mn = tx; mx = tx; }
            rowMin[ty] = mn; rowMax[ty] = mx;
        }
        var spans = new int[S * 2];
        for (int ty = 0; ty < S; ty++)
        {
            int mn = S, mx = -1;
            for (int k = Math.Max(0, ty - reach); k <= Math.Min(S - 1, ty + reach); k++)
                if (rowMax[k] >= 0) { mn = Math.Min(mn, rowMin[k]); mx = Math.Max(mx, rowMax[k]); }
            // A read past the art's edge clamps to the edge texel: a span that
            // reaches within the reach of an edge reaches the edge.
            spans[ty * 2] = mx < 0 ? S : Math.Max(0, mn - reach);
            spans[ty * 2 + 1] = mx < 0 ? -1 : Math.Min(S - 1, mx + reach);
        }
        _spans.AddOrUpdate(art, Tuple.Create(reach, spans));
        return spans;
    }

    /// <summary>A face layer (straight sRGB BGRA at <see cref="JiggleShader.Size"/>)
    /// over the frame where it was authored on the bust - two screen pixels a
    /// texel, nearest - moved with the bust's height.</summary>
    public static void Overlay(float[] dest, byte[] overlayStraight, int frameBottom, int bustBottom, float[]? decode = null)
    {
        const int S = JiggleShader.Size, N = FrameSize;
        var lin = decode ?? FakeGi.SrgbToLinear;
        for (int y = 0; y < N; y++)
        {
            // This frame row's screen pixel, on the bust: pixels up from its bottom.
            int up = frameBottom + (N - 1 - y) - bustBottom;
            if (up < 0 || up >= N) continue;
            int srcRow = (S - 1 - up / 2) * S * 4;
            int row = y * N * 4;
            for (int x = 0; x < N; x++)
            {
                int oi = srcRow + (x / 2) * 4;
                float a = overlayStraight[oi + 3] / 255f;
                if (a <= 0f) continue;
                int d = row + x * 4;
                float keep = 1f - a;
                dest[d] = lin[overlayStraight[oi + 2]] * a + dest[d] * keep;
                dest[d + 1] = lin[overlayStraight[oi + 1]] * a + dest[d + 1] * keep;
                dest[d + 2] = lin[overlayStraight[oi]] * a + dest[d + 2] * keep;
                dest[d + 3] = a + dest[d + 3] * keep;
            }
        }
    }

    /// <summary>One premultiplied layer over another.</summary>
    public static void Over(float[] dest, float[] layer)
    {
        for (int i = 0; i < dest.Length; i += 4)
        {
            float a = layer[i + 3];
            if (a <= 0f) continue;
            float keep = 1f - a;
            dest[i] = layer[i] + dest[i] * keep;
            dest[i + 1] = layer[i + 1] + dest[i + 1] * keep;
            dest[i + 2] = layer[i + 2] + dest[i + 2] * keep;
            dest[i + 3] = a + dest[i + 3] * keep;
        }
    }

    // ── The frame as the game makes it: the scene, then the post-processing ──

    /// <summary>What the post-processing adds at a preview quality: bloom,
    /// the anamorphic flares, and the colour (tonemapping, vignetting, URP's
    /// grading). The scene itself - room, bust, fake GI, lamps - is the game's
    /// at every quality but where the fake GI is left out.</summary>
    internal readonly record struct PostParts(bool Bloom, bool Flares, bool Colour)
    {
        public static readonly PostParts All = new(true, true, true);
        public static readonly PostParts None = new(false, false, false);
    }

    /// <summary>The scene the camera's target holds before the post-processing:
    /// the room (HDR, the game's own) over the whole screen.</summary>
    internal static GamePost.Target Scene(BackdropScene scene) => Copy(scene.Template);

    private static GamePost.Target Copy(GamePost.Target t)
    {
        var c = new GamePost.Target(t.W, t.H);
        Array.Copy(t.Px, c.Px, t.Px.Length);
        return c;
    }

    /// <summary>The scene back to the room alone over the frame - all a frame
    /// changes - for the next frame.</summary>
    internal static void ResetFrame(GamePost.Target target, BackdropScene scene)
    {
        var t = scene.Template;
        for (int y = 0; y < FrameSize; y++)
        {
            int sy = scene.FrameBottom + y;
            if (sy < 0 || sy >= t.H) continue;
            int o = (sy * t.W + scene.Left) * 4;
            Array.Copy(t.Px, o, target.Px, o, FrameSize * 4);
        }
    }

    internal static GamePost.Target BuildScene(BackdropScene scene)
    {
        var b = scene.Backdrop;
        var t = new GamePost.Target(b.ScreenWidth, b.ScreenHeight);
        var room = b.RoomHdr;
        for (int p = 0, i = 0; p < room.Length; p += 3, i += 4)
        {
            t.Px[i] = room[p]; t.Px[i + 1] = room[p + 1]; t.Px[i + 2] = room[p + 2]; t.Px[i + 3] = 1f;
        }
        return t;
    }

    /// <summary>A layer of the bust (premultiplied linear RGBA over the frame,
    /// rows top-down) blended over the scene as the GPU blends it into the
    /// camera's target - which keeps 11-11-10-bit floats, so every blend
    /// rounds. Its alpha joins <paramref name="coverage"/>.</summary>
    internal static void Layer(GamePost.Target target, BackdropScene scene, float[] layer, float[] coverage)
    {
        int w = target.W;
        Parallel.For(0, FrameSize, Options, y =>
        {
            int sy = scene.FrameBottom + (FrameSize - 1 - y);
            if (sy < 0 || sy >= target.H) return;
            for (int x = 0; x < FrameSize; x++)
            {
                int i = (y * FrameSize + x) * 4;
                float a = layer[i + 3];
                if (a <= 0f) continue;
                int o = (sy * w + scene.Left + x) * 4;
                float keep = 1f - a;
                target.Px[o] = layer[i] + target.Px[o] * keep;
                target.Px[o + 1] = layer[i + 1] + target.Px[o + 1] * keep;
                target.Px[o + 2] = layer[i + 2] + target.Px[o + 2] * keep;
                GamePost.ToFrameFormat(target.Px, o);
                int c = y * FrameSize + x;
                coverage[c] = a + coverage[c] * keep;
            }
        });
    }

    /// <summary>A face layer (straight sRGB BGRA at <see cref="JiggleShader.Size"/>)
    /// blended over the scene where it was authored on the bust - two screen
    /// pixels a texel, nearest - at the bust's height.</summary>
    internal static void OverlayLayer(GamePost.Target target, BackdropScene scene, byte[] overlayStraight, int bustBottom, float[] coverage)
    {
        const int S = JiggleShader.Size, N = FrameSize;
        var lin = scene.Decode;
        int w = target.W;
        for (int y = 0; y < N; y++)
        {
            int sy = scene.FrameBottom + (N - 1 - y);
            int up = sy - bustBottom;
            if (up < 0 || up >= N || sy < 0 || sy >= target.H) continue;
            int srcRow = (S - 1 - up / 2) * S * 4;
            for (int x = 0; x < N; x++)
            {
                int oi = srcRow + (x / 2) * 4;
                float a = overlayStraight[oi + 3] / 255f;
                if (a <= 0f) continue;
                int o = (sy * w + scene.Left + x) * 4;
                float keep = 1f - a;
                target.Px[o] = lin[overlayStraight[oi + 2]] * a + target.Px[o] * keep;
                target.Px[o + 1] = lin[overlayStraight[oi + 1]] * a + target.Px[o + 1] * keep;
                target.Px[o + 2] = lin[overlayStraight[oi]] * a + target.Px[o + 2] * keep;
                GamePost.ToFrameFormat(target.Px, o);
                int c = y * N + x;
                coverage[c] = a + coverage[c] * keep;
            }
        }
    }

    /// <summary>Whether the bust covers a screen pixel (rows from the bottom),
    /// by the coverage its layers left over the frame.</summary>
    internal static Func<int, int, bool> Covered(BackdropScene scene, float[] coverage)
        => (x, y) =>
        {
            int fx = x - scene.Left, fy = FrameSize - 1 - (y - scene.FrameBottom);
            return fx >= 0 && fx < FrameSize && fy >= 0 && fy < FrameSize && coverage[fy * FrameSize + fx] > 0f;
        };

    /// <summary>What the room's lamps add over the bust where it covers (over
    /// the room the room's own frame has it already).</summary>
    internal static void Lamps(GamePost.Target target, BackdropScene scene, float[] coverage)
    {
        var light = scene.LightFrame;
        int w = target.W;
        Parallel.For(0, FrameSize, Options, y =>
        {
            int sy = scene.FrameBottom + (FrameSize - 1 - y);
            if (sy < 0 || sy >= target.H) return;
            for (int x = 0; x < FrameSize; x++)
            {
                float a = coverage[y * FrameSize + x];
                if (a <= 0f) continue;
                int j = (y * FrameSize + x) * 3, o = (sy * w + scene.Left + x) * 4;
                target.Px[o] += light[j] * a; target.Px[o + 1] += light[j + 1] * a; target.Px[o + 2] += light[j + 2] * a;
                GamePost.ToFrameFormat(target.Px, o);
            }
        });
    }

    /// <summary>The frame as the screen shows it (BGRA, opaque, rows
    /// top-down): the scene through the post-processing's parts asked for,
    /// cut to the frame.</summary>
    internal static void Present(GamePost.Target target, BackdropScene scene, PostParts parts, byte[] output, GamePost.Work? work = null)
    {
        var b = scene.Backdrop;
        GamePost.Target? bloom = parts.Bloom ? GamePost.BloomAndFlaresFast(target, b.Post, parts.Flares, work ?? new GamePost.Work()) : null;
        int x0 = scene.Left, y0 = scene.FrameBottom;
        Parallel.For(0, FrameSize, Options, y =>
        {
            int sy = y0 + (FrameSize - 1 - y);
            for (int x = 0; x < FrameSize; x++)
            {
                int o = (y * FrameSize + x) * 4;
                if (sy < 0 || sy >= target.H) { output[o] = output[o + 1] = output[o + 2] = 0; output[o + 3] = 255; continue; }
                int sx = x0 + x;
                float r, g, bl;
                if (parts.Colour)
                    (r, g, bl) = GamePost.ComposeAt(target, bloom, b.Post, sx, sy, b.GradingTable);
                else
                {
                    int i = (sy * target.W + sx) * 4;
                    r = target.Px[i]; g = target.Px[i + 1]; bl = target.Px[i + 2];
                }
                output[o] = ToSrgbByte(bl); output[o + 1] = ToSrgbByte(g); output[o + 2] = ToSrgbByte(r); output[o + 3] = 255;
            }
        });
    }

    /// <summary>The frame as the screen shows it: what the room's lights add to
    /// the bust where it covers, the room behind, in sRGB (BGRA, opaque).</summary>
    public static void Finish(float[] bust, BackdropScene scene, byte[] output)
    {
        var room = scene.RoomFrame;
        var light = scene.LightFrame;
        var roomBytes = scene.RoomBytes;
        Parallel.For(0, FrameSize, Options, y =>
        {
            for (int x = 0, i = y * FrameSize * 4, j = y * FrameSize * 3; x < FrameSize; x++, i += 4, j += 3)
            {
                float a = bust[i + 3], keep = 1f - a;
                if (a <= 0f)
                {
                    // Only the room: its own numbers (they survive the trip
                    // through linear light exactly - BackdropRenderTests).
                    output[i] = roomBytes[i]; output[i + 1] = roomBytes[i + 1]; output[i + 2] = roomBytes[i + 2]; output[i + 3] = 255;
                    continue;
                }
                float r = bust[i] + (light[j] * a + room[j] * keep);
                float g = bust[i + 1] + (light[j + 1] * a + room[j + 1] * keep);
                float b = bust[i + 2] + (light[j + 2] * a + room[j + 2] * keep);
                output[i] = ToSrgbByte(b); output[i + 1] = ToSrgbByte(g); output[i + 2] = ToSrgbByte(r); output[i + 3] = 255;
            }
        });
    }

    private static readonly byte[] SrgbBytes = BuildSrgbBytes();

    /// <summary>The linear-to-screen table <see cref="ToSrgbByte"/> reads (for the graphics card's copy).</summary>
    internal static ReadOnlySpan<byte> SrgbTable => SrgbBytes;
    private const int SrgbSteps = 65536;

    private static byte[] BuildSrgbBytes()
    {
        var t = new byte[SrgbSteps + 1];
        for (int i = 0; i <= SrgbSteps; i++)
        {
            double c = (i + 0.5) / SrgbSteps;
            double s = c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
            t[i] = (byte)Math.Clamp((int)Math.Round(s * 255.0), 0, 255);
        }
        return t;
    }

    /// <summary>A linear value as the screen shows it: sRGB, rounded, clamped.</summary>
    public static byte ToSrgbByte(float linear)
    {
        if (!(linear > 0f)) return 0;
        if (linear >= 1f) return 255;
        return SrgbBytes[(int)(linear * SrgbSteps)];
    }
}

/// <summary>
/// A backdrop placed for the bust preview: the frame is the game's screen
/// around a single bust (its art rect's left edge, the screen's bottom), and
/// what does not change from frame to frame over it - the room in linear
/// light and what its lights add - worked out once. The fake GI comes with it
/// once its table is made (<see cref="Prepare"/>, off the UI thread).
/// </summary>
internal sealed class BackdropScene
{
    public PreviewBackdrop Backdrop { get; }
    public int Left { get; }
    public int FrameBottom { get; }
    /// <summary>RGB, rows top-down, over the frame.</summary>
    public float[] RoomFrame { get; }
    public float[] LightFrame { get; }
    /// <summary>The room's own BGRA over the frame, opaque.</summary>
    public byte[] RoomBytes { get; }

    private FakeGi? _gi;
    /// <summary>The fake GI, null until <see cref="Prepare"/> has made it.</summary>
    public FakeGi? Gi => System.Threading.Volatile.Read(ref _gi);

    private GameLamp? _lamp;
    /// <summary>The room's lamp with a bust's shadow in it (<see cref="GameLamp"/>),
    /// null until <see cref="Prepare"/> has made it, and for a backdrop without one.</summary>
    public GameLamp? Lamp => System.Threading.Volatile.Read(ref _lamp);

    private GamePost.Target? _template;
    /// <summary>The room as the camera's target holds it, the whole screen.</summary>
    internal GamePost.Target Template => _template ??= BackdropRender.BuildScene(this);

    /// <summary>
    /// How the game reads an 8-bit sRGB texel - the bust's art - into linear
    /// light: the game's GPU's own table when the backdrop has it (captured
    /// from the game), else the textbook curve. They differ by up to 0.004,
    /// which is a whole step of the camera's 11-bit target at a quarter of the
    /// bust's colours: with the textbook curve 41% of a bust's pixels came out
    /// as the game drew them, with the game's table all of them (1.9.1, InGameSuiteTests).
    /// </summary>
    public float[] Decode { get; }

    public BackdropScene(PreviewBackdrop backdrop)
    {
        Backdrop = backdrop;
        Decode = backdrop.LookupDecode ?? FakeGi.SrgbToLinear;
        Left = backdrop.BustLeft;
        FrameBottom = 0;
        const int N = BackdropRender.FrameSize;
        int w = backdrop.ScreenWidth, h = backdrop.ScreenHeight;
        var lin = FakeGi.SrgbToLinear;
        RoomFrame = new float[N * N * 3];
        LightFrame = new float[N * N * 3];
        RoomBytes = new byte[N * N * 4];
        for (int y = 0; y < N; y++)
        {
            int sy = FrameBottom + (N - 1 - y);
            int roomRow = (h - 1 - Math.Clamp(sy, 0, h - 1)) * w;
            for (int x = 0; x < N; x++)
            {
                int j = (y * N + x) * 3, ri = (roomRow + Math.Clamp(Left + x, 0, w - 1)) * 4;
                RoomFrame[j] = lin[backdrop.Room[ri + 2]]; RoomFrame[j + 1] = lin[backdrop.Room[ri + 1]]; RoomFrame[j + 2] = lin[backdrop.Room[ri]];
                int bi = (y * N + x) * 4;
                RoomBytes[bi] = backdrop.Room[ri]; RoomBytes[bi + 1] = backdrop.Room[ri + 1]; RoomBytes[bi + 2] = backdrop.Room[ri + 2]; RoomBytes[bi + 3] = 255;
                var (lr, lg, lb) = backdrop.LightAt(Left + x + 0.5f, sy + 0.5f);
                LightFrame[j] = lr; LightFrame[j + 1] = lg; LightFrame[j + 2] = lb;
            }
        }
    }

    /// <summary>Make the lamp, the fake GI and its blur table over the frame
    /// and the bust's breathing (about half a second the first time).</summary>
    public FakeGi Prepare()
    {
        var gi = Gi;
        if (gi != null) return gi;
        if (Backdrop.Lamp != null && Backdrop.RoomNoGlowHdr != null && Lamp == null)
            System.Threading.Volatile.Write(ref _lamp, new GameLamp(Backdrop));
        gi = new FakeGi(Backdrop);
        gi.PrepareTable(Left, FrameBottom, BackdropRender.FrameSize, BackdropRender.FrameSize);
        System.Threading.Volatile.Write(ref _gi, gi);
        return gi;
    }
}

