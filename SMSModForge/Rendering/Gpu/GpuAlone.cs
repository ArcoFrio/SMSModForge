using System;
using System.Runtime.InteropServices;
using SMSModForge.Model;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SMSModForge.Rendering.Gpu;

/// <summary>
/// <c>JiggleShader.Render</c> on the graphics card (1.9.1): a bust with no backdrop, an NPC's pose - the
/// game's own bust program, its fake GI off, at the game's density, into the
/// same bytes (Programs/JiggleAlone.hlsl). One per preview.
/// </summary>
internal sealed class GpuAlone : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly ID3D11ComputeShader _kernel;
    private readonly GpuConstants _cb0, _cb1, _cb;
    private GpuTexture? _art, _mask;
    private byte[]? _artOf, _maskOf;
    private int _artW, _artH;
    private float[]? _artFloats, _maskFloats;
    private ID3D11Buffer? _output, _outputBack;
    private ID3D11UnorderedAccessView? _outputView;
    private ID3D11ShaderResourceView? _outputRead;
    private int _outW, _outH;

    /// <summary>The picture last drawn, where it is on the card (premultiplied BGRA bytes, rows from the top) and
    /// its size - for the place's scene drawn there (<see cref="GpuPlaceScene"/>, 1.10.0). Read holding the gate.</summary>
    public (ID3D11ShaderResourceView? Bytes, int W, int H) OnCard => (_outputRead, _outW, _outH);

    /// <summary>The program's constants with the fake GI off (the rest as a
    /// backdrop's: the screen, the light white, HDR emulation 1).</summary>
    private static readonly Lazy<float[]> Cb0Alone = new(() =>
        new FakeGi(new FakeGiSettings { Enabled = 0f, Intensity = 0f }, 960, 540, new float[] { 0f, 0f, 0f, 1f }, 1, 1).Cb0);

    private GpuAlone(GpuDevice gpu)
    {
        _gpu = gpu;
        string source = GpuPrograms.Source("JiggleAlone").Replace("#include \"GameBustProgram\"", GpuPrograms.Source("GameBustProgram"));   // English on purpose: HLSL source text, not words
        _kernel = gpu.Kernel(source, "Main", "JiggleAlone");
        _cb0 = new GpuConstants(gpu, GameBustProgram.Cb0Size);
        _cb1 = new GpuConstants(gpu, GameBustProgram.Cb1Size);
        _cb = new GpuConstants(gpu, 12);
    }

    /// <summary>One for a preview, or null when the PC has no card to draw on.</summary>
    public static GpuAlone? TryCreate()
    {
        var gpu = GpuDevice.Shared;
        if (gpu == null) return null;
        lock (gpu.Gate) return new GpuAlone(gpu);
    }

    private static void AsRead(byte[] bgra, int w, int h, bool colour, float[] into)
    {
        var lin = FakeGi.SrgbToLinear;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = ((h - 1 - y) * w + x) * 4, o = (y * w + x) * 4;
                into[o] = colour ? lin[bgra[i + 2]] : bgra[i + 2] / 255f;
                into[o + 1] = colour ? lin[bgra[i + 1]] : bgra[i + 1] / 255f;
                into[o + 2] = colour ? lin[bgra[i]] : bgra[i] / 255f;
                into[o + 3] = bgra[i + 3] / 255f;
            }
    }

    /// <summary>As JiggleShader.Render: <paramref name="baseTex"/> and
    /// <paramref name="maskTex"/> (BGRA, <paramref name="srcW"/> x <paramref name="srcH"/>,
    /// rows from the top) into <paramref name="output"/> (premultiplied BGRA,
    /// <paramref name="outW"/> x <paramref name="outH"/>).</summary>
    public void Draw(byte[] baseTex, byte[] maskTex, int srcW, int srcH, JiggleParams p, (float r, float g, float b, float a) tint, float time,
                     byte[] output, int outW, int outH, int zoom)
        => Draw(baseTex, maskTex, srcW, srcH, p, tint, time, output, outW, outH, new JiggleShader.Sampling(zoom, zoom, 1));

    private byte[]? _coarse;

    /// <summary>As JiggleShader.Render with a <see cref="JiggleShader.Sampling"/>:
    /// a coarser grid drawn on the card, its pixels filling their blocks here.
    /// <paramref name="maskStill"/>: the mask is not being painted (it came from the pack's file), so it is
    /// read and sent to the card once and not with every frame.</summary>
    public void Draw(byte[] baseTex, byte[] maskTex, int srcW, int srcH, JiggleParams p, (float r, float g, float b, float a) tint, float time,
                     byte[] output, int outW, int outH, JiggleShader.Sampling s, bool maskStill = false)
    {
        if (s.Step == 1) { Sampled(baseTex, maskTex, srcW, srcH, p, tint, time, output, outW, outH, s.Across, s.Down, maskStill); return; }
        int cw = (outW + s.Step - 1) / s.Step, ch = (outH + s.Step - 1) / s.Step;
        if (_coarse == null || _coarse.Length != cw * ch * 4) _coarse = new byte[cw * ch * 4];
        Sampled(baseTex, maskTex, srcW, srcH, p, tint, time, _coarse, cw, ch, s.Across, s.Down, maskStill);
        JiggleShader.Enlarge(_coarse, cw, ch, output, outW, outH, s.Step);
    }

    /// <summary>As the Draw above, the picture left on the card (<see cref="OnCard"/>) and not brought back: at
    /// a coarser sampling its coarse grid, which whoever reads it reads by the nearest pixel (its pixels filling
    /// their blocks).</summary>
    public void DrawOnCard(byte[] baseTex, byte[] maskTex, int srcW, int srcH, JiggleParams p, (float r, float g, float b, float a) tint, float time,
                           int outW, int outH, JiggleShader.Sampling s, bool maskStill = false)
    {
        int cw = (outW + s.Step - 1) / s.Step, ch = (outH + s.Step - 1) / s.Step;
        lock (_gpu.Gate) Run(baseTex, maskTex, srcW, srcH, p, tint, time, cw, ch, s.Across, s.Down, maskStill);
    }

    private void Sampled(byte[] baseTex, byte[] maskTex, int srcW, int srcH, JiggleParams p, (float r, float g, float b, float a) tint, float time,
                         byte[] output, int outW, int outH, int zoomX, int zoomY, bool maskStill)
    {
        lock (_gpu.Gate)
        {
            var ctx = Run(baseTex, maskTex, srcW, srcH, p, tint, time, outW, outH, zoomX, zoomY, maskStill);
            ctx.CopyResource(_outputBack!, _output!);
            var map = ctx.Map(_outputBack!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try { Marshal.Copy(map.DataPointer, output, 0, outW * outH * 4); }
            finally { ctx.Unmap(_outputBack!, 0); }
        }
    }

    private ID3D11DeviceContext Run(byte[] baseTex, byte[] maskTex, int srcW, int srcH, JiggleParams p, (float r, float g, float b, float a) tint, float time,
                                    int outW, int outH, int zoomX, int zoomY, bool maskStill)
    {
        {
            var ctx = _gpu.Clean();
            int n = srcW * srcH * 4;
            if (_art == null || _artW != srcW || _artH != srcH)
            {
                _art?.Dispose(); _mask?.Dispose();
                _art = GpuTexture.Create<float>(_gpu, srcW, srcH, Format.R32G32B32A32_Float, false);
                _mask = GpuTexture.Create<float>(_gpu, srcW, srcH, Format.R32G32B32A32_Float, false);
                _artFloats = new float[n]; _maskFloats = new float[n];
                _artW = srcW; _artH = srcH; _artOf = null; _maskOf = null;
            }
            if (!ReferenceEquals(_artOf, baseTex))
            {
                AsRead(baseTex, srcW, srcH, true, _artFloats!);
                _art.Upload<float>(_gpu, _artFloats);
                _artOf = baseTex;
            }
            // Painted in place: every frame - unless it is the pack's file, which is read and sent once.
            if (!maskStill || !ReferenceEquals(_maskOf, maskTex))
            {
                AsRead(maskTex, srcW, srcH, false, _maskFloats!);
                _mask!.Upload<float>(_gpu, _maskFloats);
                _maskOf = maskStill ? maskTex : null;
            }
            if (_output == null || _outW != outW || _outH != outH)
            {
                _outputRead?.Dispose(); _outputView?.Dispose(); _output?.Dispose(); _outputBack?.Dispose();
                _output = _gpu.NewBuffer(new BufferDescription((uint)(outW * outH * 4), BindFlags.UnorderedAccess | BindFlags.ShaderResource, ResourceUsage.Default,
                                                                         CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
                _outputView = _gpu.Device.CreateUnorderedAccessView(_output);
                _outputRead = _gpu.Device.CreateShaderResourceView(_output);
                _outputBack = _gpu.Device.CreateBuffer(new BufferDescription((uint)(outW * outH * 4), BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
                _outW = outW; _outH = outH;
            }

            var cb0 = (float[])Cb0Alone.Value.Clone();
            cb0[15 * 4] = time / 20; cb0[15 * 4 + 1] = time; cb0[15 * 4 + 2] = time * 2; cb0[15 * 4 + 3] = time * 3;
            _cb0.Upload(_gpu, cb0);
            var cb1 = new float[GameBustProgram.Cb1Size];
            cb1[0] = 1; cb1[1] = 1;
            for (int k = 4; k < 12; k++) cb1[k] = 1;
            cb1[12] = p.Speed; cb1[13] = p.Strength; cb1[14] = p.Frequency; cb1[15] = p.NoiseScale; cb1[16] = p.NoiseSpeed; cb1[17] = p.NoiseStrength;
            _cb1.Upload(_gpu, cb1);
            bool white = tint.r == 1f && tint.g == 1f && tint.b == 1f;
            static float Lin(float c) => (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
            float U(int v) => BitConverter.Int32BitsToSingle(v);
            _cb.Upload(_gpu, new[]
            {
                U(outW), U(outH), U(zoomX), U(zoomY),
                white ? 1f : Lin(tint.r), white ? 1f : Lin(tint.g), white ? 1f : Lin(tint.b), tint.a,
                1f / (zoomX * zoomY), 0f, 0f, 0f,
            });

            ctx.CSSetShader(_kernel);
            ctx.CSSetConstantBuffer(0, _cb0.Buffer); ctx.CSSetConstantBuffer(1, _cb1.Buffer); ctx.CSSetConstantBuffer(2, _cb.Buffer);
            ctx.CSSetShaderResource(0, _art.Read); ctx.CSSetShaderResource(1, _mask.Read);
            ctx.CSSetUnorderedAccessView(0, _outputView!);
            ctx.Dispatch((uint)((outW + 15) / 16), (uint)((outH + 15) / 16), 1);
            ctx.CSSetUnorderedAccessView(0, null);
            ctx.CSSetShaderResource(0, null); ctx.CSSetShaderResource(1, null);
            return ctx;
        }
    }

    public void Dispose()
    {
        lock (_gpu.Gate)
        {
            _art?.Dispose(); _mask?.Dispose();
            _outputRead?.Dispose(); _outputView?.Dispose(); _output?.Dispose(); _outputBack?.Dispose();
            _cb0.Dispose(); _cb1.Dispose(); _cb.Dispose();
        }
    }
}
