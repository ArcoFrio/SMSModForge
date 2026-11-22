using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SMSModForge.Rendering.Gpu;

/// <summary>
/// <see cref="MilkingShader.Render"/> on the graphics card (1.9.1): a place's
/// level art moved by its mask (Programs/Milking.hlsl), into the same bytes.
/// One per moving layer of a place preview.
/// </summary>
internal sealed class GpuMilking : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly ID3D11ComputeShader _kernel;
    private readonly GpuConstants _cb;
    private GpuTexture? _art, _mask;
    private byte[]? _artOf, _maskOf;
    private int _artW, _artH, _maskW, _maskH, _outW, _outH;
    private ID3D11Buffer? _output, _outputBack;
    private ID3D11UnorderedAccessView? _outputView;
    private ID3D11ShaderResourceView? _outputRead;

    private GpuMilking(GpuDevice gpu)
    {
        _gpu = gpu;
        _kernel = gpu.Kernel(GpuPrograms.Source("Milking"), "Main", "Milking");
        _cb = new GpuConstants(gpu, 16);
    }

    /// <summary>One for a layer, or null when the PC has no card to draw on.</summary>
    public static GpuMilking? TryCreate()
    {
        var gpu = GpuDevice.Shared;
        if (gpu == null) return null;
        lock (gpu.Gate) return new GpuMilking(gpu);
    }

    /// <summary>The picture last drawn, where it is on the card (premultiplied BGRA bytes, rows from the top) and
    /// its size - for the place's scene drawn there (<see cref="GpuPlaceScene"/>, 1.10.0). Read holding the gate.</summary>
    public (ID3D11ShaderResourceView? Bytes, int W, int H) OnCard => (_outputRead, _outW, _outH);

    /// <summary>As <see cref="MilkingShader.Render"/>. <paramref name="maskStill"/>: the mask is not being painted
    /// (it came from the pack's file), so it is sent to the card once and not with every frame.</summary>
    public void Draw(byte[] baseTex, int srcW, int srcH, byte[] maskTex, int maskW, int maskH,
                     in MilkingShader.Settings s, float time, byte[] output, int outW, int outH, bool maskStill = false)
    {
        lock (_gpu.Gate)
        {
            var ctx = Run(baseTex, srcW, srcH, maskTex, maskW, maskH, s, time, outW, outH, maskStill);
            ctx.CopyResource(_outputBack!, _output!);
            var map = ctx.Map(_outputBack!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try { Marshal.Copy(map.DataPointer, output, 0, outW * outH * 4); }
            finally { ctx.Unmap(_outputBack!, 0); }
        }
    }

    /// <summary>As <see cref="Draw"/>, the picture left on the card (<see cref="OnCard"/>) and not brought back.</summary>
    public void DrawOnCard(byte[] baseTex, int srcW, int srcH, byte[] maskTex, int maskW, int maskH,
                           in MilkingShader.Settings s, float time, int outW, int outH, bool maskStill = false)
    {
        lock (_gpu.Gate) Run(baseTex, srcW, srcH, maskTex, maskW, maskH, s, time, outW, outH, maskStill);
    }

    private ID3D11DeviceContext Run(byte[] baseTex, int srcW, int srcH, byte[] maskTex, int maskW, int maskH,
                                    in MilkingShader.Settings s, float time, int outW, int outH, bool maskStill)
    {
        float dx = s.DirX, dy = s.DirY;
        float len2 = dx * dx + dy * dy;
        if (MathF.Sqrt(len2) < 0.1f) { dx = 0f; dy = 1f; }
        else { float inv = 1f / MathF.Sqrt(len2); dx *= inv; dy *= inv; }
        float timePhase = time * s.Speed + s.PhaseOffset;
        var ctx = _gpu.Clean();
        if (_art == null || _artW != srcW || _artH != srcH)
        {
            _art?.Dispose();
            _art = GpuTexture.Create<byte>(_gpu, srcW, srcH, Format.R8G8B8A8_UInt, false);
            _artW = srcW; _artH = srcH; _artOf = null;
        }
        if (!ReferenceEquals(_artOf, baseTex)) { _art.Upload<byte>(_gpu, baseTex); _artOf = baseTex; }
        if (_mask == null || _maskW != maskW || _maskH != maskH)
        {
            _mask?.Dispose();
            _mask = GpuTexture.Create<byte>(_gpu, maskW, maskH, Format.R8G8B8A8_UInt, false);
            _maskW = maskW; _maskH = maskH; _maskOf = null;
        }
        // Painted in place: every frame - unless it is the pack's file, which is sent once.
        if (!maskStill || !ReferenceEquals(_maskOf, maskTex)) { _mask.Upload<byte>(_gpu, maskTex); _maskOf = maskStill ? maskTex : null; }
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
        float U(int v) => BitConverter.Int32BitsToSingle(v);
        _cb.Upload(_gpu, new[]
        {
            U(srcW), U(srcH), U(maskW), U(maskH), U(outW), U(outH), timePhase, s.AmplitudeVariation,
            s.Strength, s.WaveCount, s.CompressionRatio, dx, dy, 0f, 0f, 0f,
        });
        ctx.CSSetShader(_kernel);
        ctx.CSSetConstantBuffer(0, _cb.Buffer);
        ctx.CSSetShaderResource(0, _art.Read); ctx.CSSetShaderResource(1, _mask.Read);
        ctx.CSSetUnorderedAccessView(0, _outputView!);
        ctx.Dispatch((uint)((outW + 15) / 16), (uint)((outH + 15) / 16), 1);
        ctx.CSSetUnorderedAccessView(0, null);
        ctx.CSSetShaderResource(0, null); ctx.CSSetShaderResource(1, null);
        return ctx;
    }

    public void Dispose()
    {
        lock (_gpu.Gate)
        {
            _art?.Dispose(); _mask?.Dispose();
            _outputRead?.Dispose(); _outputView?.Dispose(); _output?.Dispose(); _outputBack?.Dispose();
            _cb.Dispose();
        }
    }
}
