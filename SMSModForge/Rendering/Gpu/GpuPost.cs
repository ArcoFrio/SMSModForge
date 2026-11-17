using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SMSModForge.Rendering.Gpu;

/// <summary>
/// <see cref="BackdropRender.Present"/> on the graphics card (1.9.1): the
/// frame (the camera's target, as the processor composed it) through
/// Beautify's bloom and flares - the game's own passes, in the order and at
/// the sizes <see cref="GamePost.BloomAndFlares"/> runs them - its compose,
/// URP's grading and the screen's sRGB, into the preview's bytes
/// (Programs/Post.hlsl). One per preview.
/// </summary>
internal sealed class GpuPost : IDisposable
{
    private const int N = BackdropRender.FrameSize;
    private readonly GpuDevice _gpu;
    private readonly ID3D11ComputeShader _blit, _present;
    private readonly GpuConstants _cb0, _cb1, _blitCb, _presentCb;
    private readonly ID3D11Buffer _output, _outputBack, _srgb;
    private readonly ID3D11UnorderedAccessView _outputView;
    private readonly ID3D11ShaderResourceView _srgbView;
    private readonly GpuTexture _noBloom;
    private GpuTexture? _frame, _table;
    private float[]? _tableOf;
    private readonly Dictionary<(int, int, int), GpuTexture> _targets = new();

    private GpuPost(GpuDevice gpu)
    {
        _gpu = gpu;
        string source = GpuPrograms.Source("Post").Replace("#include \"GamePostProgram\"", GpuPrograms.Source("GamePostProgram"));   // English on purpose: HLSL source text, not words
        _blit = gpu.Kernel(source, "Blit", "Post");
        _present = gpu.Kernel(source, "Present", "Post");
        _cb0 = new GpuConstants(gpu, 178 * 4);
        _cb1 = new GpuConstants(gpu, 20);
        _cb1.Upload(gpu, new float[20]);
        _blitCb = new GpuConstants(gpu, 8);
        _presentCb = new GpuConstants(gpu, 20);
        _output = gpu.NewBuffer(new BufferDescription((uint)(N * N * 4), BindFlags.UnorderedAccess, ResourceUsage.Default,
                                                                CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
        _outputView = gpu.Device.CreateUnorderedAccessView(_output);
        _outputBack = gpu.Device.CreateBuffer(new BufferDescription((uint)(N * N * 4), BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        var table = BackdropRender.SrgbTable;
        var words = new uint[table.Length];
        for (int i = 0; i < words.Length; i++) words[i] = table[i];
        unsafe
        {
            fixed (uint* p = words)
                _srgb = gpu.Device.CreateBuffer(new BufferDescription((uint)(words.Length * 4), BindFlags.ShaderResource, ResourceUsage.Default,
                                                                      CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4), new SubresourceData((IntPtr)p));
        }
        _srgbView = gpu.Device.CreateShaderResourceView(_srgb);
        // No bloom: a picture of none, made so - the last pass reads it with the bloom off, and
        // one made empty held whatever the card's memory held (1.9.1: the bust preview with
        // its bloom off came out washed over now and then, beside other previews).
        _noBloom = GpuTexture.Create<ushort>(gpu, 1, 1, Format.R16G16B16A16_Float, false, new ushort[4]);
    }

    /// <summary>One for a preview, or null when the PC has no card to draw on.</summary>
    public static GpuPost? TryCreate()
    {
        var gpu = GpuDevice.Shared;
        if (gpu == null) return null;
        lock (gpu.Gate) return new GpuPost(gpu);
    }

    private GpuTexture TargetOf(int w, int h, int slot)
    {
        if (!_targets.TryGetValue((w, h, slot), out var t))
            _targets[(w, h, slot)] = t = GpuTexture.Create<float>(_gpu, w, h, Format.R16G16B16A16_Float, true);
        return t;
    }

    private enum Reads { Centre, BlurAcross, BlurUpDown, Cross }
    private enum Program { BloomLuminance, Resample, ResampleAndCombine, FlaresLuminance, FlaresResample, Combine, Blur }

    private void Blit(GpuTexture dest, GpuTexture src, GpuTexture? second, Reads reads, float[] cb0, Program program)
    {
        var ctx = _gpu.Context;
        _cb0.Upload(_gpu, cb0);
        float U(int v) => BitConverter.Int32BitsToSingle(v);
        _blitCb.Upload(_gpu, new[] { U(dest.W), U(dest.H), U((int)reads), U((int)program), 1f / src.W, 1f / src.H, U(0), 0f });
        ctx.CSSetShader(_blit);
        ctx.CSSetConstantBuffer(0, _cb0.Buffer); ctx.CSSetConstantBuffer(1, _cb1.Buffer); ctx.CSSetConstantBuffer(2, _blitCb.Buffer);
        ctx.CSSetShaderResource(0, src.Read); ctx.CSSetShaderResource(1, (second ?? src).Read);
        ctx.CSSetUnorderedAccessView(0, dest.Write!);
        ctx.Dispatch((uint)((dest.W + 7) / 8), (uint)((dest.H + 7) / 8), 1);
        ctx.CSSetUnorderedAccessView(0, null);
        ctx.CSSetShaderResource(0, null); ctx.CSSetShaderResource(1, null);
    }

    /// <summary>Beautify's bloom and flares from the frame on the card, as
    /// <see cref="GamePost.BloomAndFlares"/>: the target its final pass reads.</summary>
    private GpuTexture BloomAndFlares(GpuTexture frame, GamePost.Settings s, bool flares)
    {
        int levels = s.Iterations;
        float aspect = (float)frame.H / frame.W;
        int w0 = (int)(Lerp(512f, frame.W, s.BloomResolution / 10f) / 4f) * 4;
        var down = new GpuTexture[levels + 1]; var up = new GpuTexture[levels + 1];
        int bw = w0;
        for (int i = 0; i <= levels; i++)
        {
            int h = Math.Max(1, (int)(bw * aspect));
            down[i] = TargetOf(bw, h, 0); up[i] = TargetOf(bw, h, 1);
            bw /= 2;
        }
        var lumCb = GamePost.PassConstants((140, s.BloomIntensity, 0f, 0f, s.BloomThreshold), (142, 0f, 0f, s.BloomMaxBrightness, 0f),
                                           (143, s.BloomTint.r, s.BloomTint.g, s.BloomTint.b, s.BloomTint.a));
        Blit(down[0], frame, null, Reads.Centre, lumCb, Program.BloomLuminance);
        var none = GamePost.PassConstants();
        for (int k = 0; k < levels; k++)
        {
            Blit(down[k + 1], down[k], null, Reads.Cross, none, Program.Resample);
            var temp = TargetOf(down[k + 1].W, down[k + 1].H, 2);
            Blit(temp, down[k + 1], null, Reads.BlurAcross, none, Program.Blur);
            Blit(down[k + 1], temp, null, Reads.BlurUpDown, none, Program.Blur);
        }
        var spreadCb = GamePost.PassConstants((149, 0f, 0f, 0f, s.BloomSpread));
        var dest = down[levels];
        for (int i = levels; i > 0; i--)
        {
            Blit(up[i - 1], dest, down[i - 1], Reads.Cross, spreadCb, Program.ResampleAndCombine);
            dest = up[i - 1];
        }
        if (!flares) return dest;

        int f0 = (int)(Lerp(512f, frame.W, s.FlaresResolution / 10f) / 4f) * 4;
        float shrink = 1.7777778f * s.FlaresSpread * f0 / 512f;
        var fdown = new GpuTexture[levels + 1]; var fup = new GpuTexture[levels + 1];
        int fw = f0;
        for (int l = 0; l <= levels; l++)
        {
            int across = Math.Max(1, (int)(fw / shrink));
            int w = s.FlaresVertical ? f0 : across, h = s.FlaresVertical ? across : f0;
            fdown[l] = TargetOf(w, h, 3); fup[l] = TargetOf(w, h, 4);
            fw /= 2;
        }
        var flCb = GamePost.PassConstants((152, s.FlaresIntensity, s.FlaresThreshold, 0f, s.FlaresMaxBrightness));
        // BlurOne: into a new target, which takes the old one's place.
        GpuTexture BlurOne(GpuTexture t, int slot)
        {
            var temp = TargetOf(t.W, t.H, slot);
            Blit(temp, t, null, s.FlaresVertical ? Reads.BlurUpDown : Reads.BlurAcross, none, Program.Blur);
            return temp;
        }
        Blit(fdown[0], frame, null, Reads.Centre, flCb, Program.FlaresLuminance);
        fdown[0] = BlurOne(fdown[0], 5);
        for (int n = 0; n < levels; n++)
        {
            Blit(fdown[n + 1], fdown[n], null, Reads.Cross, none, Program.Resample);
            fdown[n + 1] = BlurOne(fdown[n + 1], 6 + (n & 1));
        }
        var frCb = GamePost.PassConstants((150, s.FlaresTint.r, s.FlaresTint.g, s.FlaresTint.b, s.FlaresTint.a), (152, s.FlaresIntensity, s.FlaresThreshold, 0f, s.FlaresMaxBrightness),
                                          (149, 0f, 0f, 0f, s.BloomSpread));
        var fdest = fdown[levels];
        for (int i = levels; i > 0; i--)
        {
            if (i == 1) Blit(fup[i - 1], fdest, fdown[i], Reads.Cross, frCb, Program.FlaresResample);
            else Blit(fup[i - 1], fdest, fdown[i], Reads.Cross, spreadCb, Program.ResampleAndCombine);
            fdest = fup[i - 1];
        }
        Blit(down[0], fdest, dest, Reads.Centre, none, Program.Combine);
        return down[0];
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    /// <summary>As <see cref="BackdropRender.Present"/>: the frame as the screen shows it.</summary>
    public void Present(GamePost.Target target, BackdropScene scene, BackdropRender.PostParts parts, byte[] output)
    {
        var b = scene.Backdrop;
        lock (_gpu.Gate)
        {
            var ctx = _gpu.Clean();
            if (_frame == null || _frame.W != target.W || _frame.H != target.H)
            {
                _frame?.Dispose();
                _frame = GpuTexture.Create<float>(_gpu, target.W, target.H, Format.R32G32B32A32_Float, false);
            }
            _frame.Upload<float>(_gpu, target.Px);
            PresentFrom(_frame, scene, parts, output);
        }
    }

    /// <summary>As <see cref="Present"/>, from a frame already on the card.
    /// The caller holds the device's gate.</summary>
    internal void PresentFrom(GpuTexture frame, BackdropScene scene, BackdropRender.PostParts parts, byte[] output)
    {
        var b = scene.Backdrop;
        {
            var ctx = _gpu.Context;
            if (!ReferenceEquals(_tableOf, b.GradingTable))
            {
                _table?.Dispose();
                int tw = (int)MathF.Round(1f / b.Post.LutParams.x), th = (int)MathF.Round(1f / b.Post.LutParams.y);
                _table = GpuTexture.Create<float>(_gpu, tw, th, Format.R32G32B32A32_Float, false, b.GradingTable);
                _tableOf = b.GradingTable;
            }
            var bloom = parts.Bloom ? BloomAndFlares(frame, b.Post, parts.Flares) : _noBloom;

            _cb0.Upload(_gpu, GamePost.ComposeConstants(b.Post));
            float U(int v) => BitConverter.Int32BitsToSingle(v);
            var lut = b.Post.LutParams;
            _presentCb.Upload(_gpu, new[] { U(scene.Left), U(scene.FrameBottom), U(parts.Colour ? 1 : 0), U(parts.Bloom ? 1 : 0), lut.x, lut.y, lut.z, lut.w,
                                            U(N), U(N), U(0), 0f, 0f, 0f, 0f, 0f, U(0), 0f, 0f, 0f });
            _blitCb.Upload(_gpu, new[] { U(0), U(0), U(0), U(0), 0f, 0f, U(1), 0f });   // the compose pass reads the frame by the texel
            ctx.CSSetShader(_present);
            ctx.CSSetConstantBuffer(0, _cb0.Buffer); ctx.CSSetConstantBuffer(1, _cb1.Buffer);
            ctx.CSSetConstantBuffer(2, _blitCb.Buffer); ctx.CSSetConstantBuffer(3, _presentCb.Buffer);
            ctx.CSSetShaderResource(0, frame.Read); ctx.CSSetShaderResource(1, bloom.Read);
            ctx.CSSetShaderResource(2, _table!.Read); ctx.CSSetShaderResource(3, _srgbView);
            ctx.CSSetUnorderedAccessView(1, _outputView);
            ctx.Dispatch(N / 16, N / 16, 1);
            ctx.CSSetUnorderedAccessView(1, null);
            ctx.CSSetShaderResource(0, null); ctx.CSSetShaderResource(1, null);
            ctx.CopyResource(_outputBack, _output);
            var map = ctx.Map(_outputBack, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try { Marshal.Copy(map.DataPointer, output, 0, N * N * 4); }
            finally { ctx.Unmap(_outputBack, 0); }
        }
    }

    private GpuTexture? _lookFrame;
    private ID3D11Buffer? _lookOut, _lookBack;
    private ID3D11UnorderedAccessView? _lookOutView;
    private float[]? _lookFloats;

    /// <summary>
    /// The game's look over a place on the card (1.9.1): as GamePostFast.LookOver on
    /// the processor - the place as the preview drew it (<paramref name="inputPbgra"/>,
    /// <paramref name="w"/> x <paramref name="h"/>, rows from the top) into the
    /// camera's target's format, the bloom and flares, the colour - with
    /// <paramref name="scene"/>'s settings, into <paramref name="output"/> (BGRA, the
    /// whole screen). With the colour off the bloom is still added, as the place
    /// preview's tick boxes have it.
    /// </summary>
    public void LookOver(byte[] inputPbgra, int w, int h, BackdropScene scene, BackdropRender.PostParts parts, byte[] output)
    {
        var b = scene.Backdrop;
        var decode = scene.Decode;
        var floats = _lookFloats ??= new float[w * h * 4];
        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            int src = (h - 1 - y) * w * 4;
            for (int x = 0; x < w; x++)
            {
                int i = src + x * 4, o = (y * w + x) * 4;
                floats[o] = decode[inputPbgra[i + 2]]; floats[o + 1] = decode[inputPbgra[i + 1]]; floats[o + 2] = decode[inputPbgra[i]]; floats[o + 3] = 1f;
                GamePost.ToFrameFormat(floats, o);
            }
        });
        lock (_gpu.Gate)
        {
            var ctx = _gpu.Clean();
            if (_lookFrame == null || _lookFrame.W != w || _lookFrame.H != h)
            {
                _lookFrame?.Dispose(); _lookOutView?.Dispose(); _lookOut?.Dispose(); _lookBack?.Dispose();
                _lookFrame = GpuTexture.Create<float>(_gpu, w, h, Format.R32G32B32A32_Float, false);
                _lookOut = _gpu.NewBuffer(new BufferDescription((uint)(w * h * 4), BindFlags.UnorderedAccess, ResourceUsage.Default,
                                                                          CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
                _lookOutView = _gpu.Device.CreateUnorderedAccessView(_lookOut);
                _lookBack = _gpu.Device.CreateBuffer(new BufferDescription((uint)(w * h * 4), BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            }
            _lookFrame.Upload<float>(_gpu, floats);
            if (!ReferenceEquals(_tableOf, b.GradingTable))
            {
                _table?.Dispose();
                int tw = (int)MathF.Round(1f / b.Post.LutParams.x), th = (int)MathF.Round(1f / b.Post.LutParams.y);
                _table = GpuTexture.Create<float>(_gpu, tw, th, Format.R32G32B32A32_Float, false, b.GradingTable);
                _tableOf = b.GradingTable;
            }
            var bloom = parts.Bloom ? BloomAndFlares(_lookFrame, b.Post, parts.Flares) : _noBloom;
            _cb0.Upload(_gpu, GamePost.ComposeConstants(b.Post));
            float U(int v) => BitConverter.Int32BitsToSingle(v);
            var lut = b.Post.LutParams;
            _presentCb.Upload(_gpu, new[] { U(0), U(0), U(parts.Colour ? 1 : 0), U(parts.Bloom ? 1 : 0), lut.x, lut.y, lut.z, lut.w,
                                            U(w), U(h), U(1), b.Post.BloomIntensity, 0f, 0f, 0f, 0f, U(0), 0f, 0f, 0f });
            _blitCb.Upload(_gpu, new[] { U(0), U(0), U(0), U(0), 0f, 0f, U(1), 0f });
            ctx.CSSetShader(_present);
            ctx.CSSetConstantBuffer(0, _cb0.Buffer); ctx.CSSetConstantBuffer(1, _cb1.Buffer);
            ctx.CSSetConstantBuffer(2, _blitCb.Buffer); ctx.CSSetConstantBuffer(3, _presentCb.Buffer);
            ctx.CSSetShaderResource(0, _lookFrame.Read); ctx.CSSetShaderResource(1, bloom.Read);
            ctx.CSSetShaderResource(2, _table!.Read); ctx.CSSetShaderResource(3, _srgbView);
            ctx.CSSetUnorderedAccessView(1, _lookOutView!);
            ctx.Dispatch((uint)((w + 15) / 16), (uint)((h + 15) / 16), 1);
            ctx.CSSetUnorderedAccessView(1, null);
            ctx.CSSetShaderResource(0, null); ctx.CSSetShaderResource(1, null);
            ctx.CopyResource(_lookBack!, _lookOut!);
            var map = ctx.Map(_lookBack!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try { Marshal.Copy(map.DataPointer, output, 0, w * h * 4); }
            finally { ctx.Unmap(_lookBack!, 0); }
        }
    }

    private GpuTexture? _viewScene;
    private ID3D11Buffer? _viewOut, _viewBack;
    private ID3D11UnorderedAccessView? _viewOutView;
    private float[]? _viewFloats;

    /// <summary>
    /// As GamePost.LookOverView on the card (1.9.1): the bloom from the whole
    /// screen at the game's size (<paramref name="screenPbgra"/>, only when the
    /// bloom is drawn), the compose and the grading at every pixel of the view
    /// (<paramref name="viewPbgra"/>, <paramref name="vw"/> x <paramref name="vh"/>,
    /// rows from the top), whose place on the screen is uv = (ax x + bx, ay y + by).
    /// </summary>
    public void LookOverView(byte[]? screenPbgra, int sw, int sh, byte[] viewPbgra, int vw, int vh, float ax, float bx, float ay, float by,
                             BackdropScene scene, BackdropRender.PostParts parts, byte[] output)
    {
        var b = scene.Backdrop;
        var decode = scene.Decode;
        float[]? screenFloats = null;
        if (parts.Bloom && screenPbgra != null)
        {
            screenFloats = _lookFloats ??= new float[sw * sh * 4];
            if (screenFloats.Length != sw * sh * 4) screenFloats = _lookFloats = new float[sw * sh * 4];
            var sf = screenFloats;
            System.Threading.Tasks.Parallel.For(0, sh, y =>
            {
                int src = (sh - 1 - y) * sw * 4;
                for (int x = 0; x < sw; x++)
                {
                    int i = src + x * 4, o = (y * sw + x) * 4;
                    sf[o] = decode[screenPbgra[i + 2]]; sf[o + 1] = decode[screenPbgra[i + 1]]; sf[o + 2] = decode[screenPbgra[i]]; sf[o + 3] = 1f;
                    GamePost.ToFrameFormat(sf, o);
                }
            });
        }
        var view = _viewFloats ??= new float[vw * vh * 4];
        if (view.Length != vw * vh * 4) view = _viewFloats = new float[vw * vh * 4];
        System.Threading.Tasks.Parallel.For(0, vh, y =>
        {
            for (int x = 0; x < vw; x++)
            {
                int i = (y * vw + x) * 4;
                view[i] = decode[viewPbgra[i + 2]]; view[i + 1] = decode[viewPbgra[i + 1]]; view[i + 2] = decode[viewPbgra[i]]; view[i + 3] = 1f;
                GamePost.ToFrameFormat(view, i);
            }
        });
        lock (_gpu.Gate)
        {
            var ctx = _gpu.Clean();
            var bloom = _noBloom;
            if (screenFloats != null)
            {
                if (_lookFrame == null || _lookFrame.W != sw || _lookFrame.H != sh)
                {
                    _lookFrame?.Dispose();
                    _lookFrame = GpuTexture.Create<float>(_gpu, sw, sh, Format.R32G32B32A32_Float, false);
                }
                _lookFrame.Upload<float>(_gpu, screenFloats);
                bloom = BloomAndFlares(_lookFrame, b.Post, parts.Flares);
            }
            if (_viewScene == null || _viewScene.W != vw || _viewScene.H != vh)
            {
                _viewScene?.Dispose(); _viewOutView?.Dispose(); _viewOut?.Dispose(); _viewBack?.Dispose();
                _viewScene = GpuTexture.Create<float>(_gpu, vw, vh, Format.R32G32B32A32_Float, false);
                _viewOut = _gpu.NewBuffer(new BufferDescription((uint)(vw * vh * 4), BindFlags.UnorderedAccess, ResourceUsage.Default,
                                                                          CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
                _viewOutView = _gpu.Device.CreateUnorderedAccessView(_viewOut);
                _viewBack = _gpu.Device.CreateBuffer(new BufferDescription((uint)(vw * vh * 4), BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            }
            _viewScene.Upload<float>(_gpu, view);
            if (!ReferenceEquals(_tableOf, b.GradingTable))
            {
                _table?.Dispose();
                int tw = (int)MathF.Round(1f / b.Post.LutParams.x), th = (int)MathF.Round(1f / b.Post.LutParams.y);
                _table = GpuTexture.Create<float>(_gpu, tw, th, Format.R32G32B32A32_Float, false, b.GradingTable);
                _tableOf = b.GradingTable;
            }
            _cb0.Upload(_gpu, GamePost.ComposeConstants(b.Post));
            float U(int v) => BitConverter.Int32BitsToSingle(v);
            var lut = b.Post.LutParams;
            _presentCb.Upload(_gpu, new[] { U(0), U(0), U(parts.Colour ? 1 : 0), U(parts.Bloom && screenFloats != null ? 1 : 0), lut.x, lut.y, lut.z, lut.w,
                                            U(vw), U(vh), U(1), b.Post.BloomIntensity, ax, bx, ay, by, U(1), 0f, 0f, 0f });
            _blitCb.Upload(_gpu, new[] { U(0), U(0), U(0), U(0), 0f, 0f, U(1), 0f });
            ctx.CSSetShader(_present);
            ctx.CSSetConstantBuffer(0, _cb0.Buffer); ctx.CSSetConstantBuffer(1, _cb1.Buffer);
            ctx.CSSetConstantBuffer(2, _blitCb.Buffer); ctx.CSSetConstantBuffer(3, _presentCb.Buffer);
            ctx.CSSetShaderResource(0, _viewScene.Read); ctx.CSSetShaderResource(1, bloom.Read);
            ctx.CSSetShaderResource(2, _table!.Read); ctx.CSSetShaderResource(3, _srgbView);
            ctx.CSSetUnorderedAccessView(1, _viewOutView!);
            ctx.Dispatch((uint)((vw + 15) / 16), (uint)((vh + 15) / 16), 1);
            ctx.CSSetUnorderedAccessView(1, null);
            ctx.CSSetShaderResource(0, null); ctx.CSSetShaderResource(1, null);
            ctx.CopyResource(_viewBack!, _viewOut!);
            var map = ctx.Map(_viewBack!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try { Marshal.Copy(map.DataPointer, output, 0, vw * vh * 4); }
            finally { ctx.Unmap(_viewBack!, 0); }
        }
    }

    /// <summary>For tests: the bloom and flares the final pass would read, back from the card.</summary>
    internal float[] BloomForTests(GamePost.Target frame, GamePost.Settings s, bool flares, out int w, out int h)
    {
        lock (_gpu.Gate)
        {
            _gpu.Clean();
            using var f = GpuTexture.Create<float>(_gpu, frame.W, frame.H, Format.R32G32B32A32_Float, false, frame.Px);
            var bloom = BloomAndFlares(f, s, flares);
            w = bloom.W; h = bloom.H;
            // The half floats as the target keeps them, widened here.
            using var back = new GpuReadback(_gpu, w, h, Format.R16G16B16A16_Float);
            var halves = new ushort[w * h * 4];
            back.Read<ushort>(_gpu, bloom, halves);
            var px = new float[halves.Length];
            for (int i = 0; i < px.Length; i++) px[i] = (float)BitConverter.UInt16BitsToHalf(halves[i]);
            return px;
        }
    }

    public void Dispose()
    {
        lock (_gpu.Gate)
        {
            foreach (var t in _targets.Values) t.Dispose();
            _frame?.Dispose(); _table?.Dispose(); _noBloom.Dispose();
            _outputView.Dispose(); _output.Dispose(); _outputBack.Dispose();
            _lookFrame?.Dispose(); _lookOutView?.Dispose(); _lookOut?.Dispose(); _lookBack?.Dispose();
            _viewScene?.Dispose(); _viewOutView?.Dispose(); _viewOut?.Dispose(); _viewBack?.Dispose();
            _srgbView.Dispose(); _srgb.Dispose();
            _cb0.Dispose(); _cb1.Dispose(); _blitCb.Dispose(); _presentCb.Dispose();
        }
    }
}
