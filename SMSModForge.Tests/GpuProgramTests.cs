using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's bust program as HLSL (Tools/Shaders/dxbc_to_hlsl.py, from the
/// instructions GameBustProgram.g.cs was made of) runs on the graphics card
/// and draws every pixel of the shader fixture as the game's program does -
/// the fixture being that program run by the reference interpreter of its
/// disassembly. The textures are read in the kernel exactly as the fixture's
/// are (point, and the interpreter's bilinear), so what is compared is the
/// program's arithmetic on the card. Direct3D lets a card round division,
/// square roots and powers a little differently (GpuDeviceTests), as the
/// game's own frames are; so: drawn or not the same everywhere, and every
/// colour within a fraction of a level. Control: with the fake GI switched
/// off in its constants, the card's frame is levels away from the fixture.
/// </summary>
public sealed class GpuProgramTests
{
    private readonly ITestOutputHelper _out;
    public GpuProgramTests(ITestOutputHelper o) => _out = o;

    internal const string Reads = @"
float4 PointRead(Texture2D<float4> t, float u, float v)
{
    uint w, h; t.GetDimensions(w, h);
    precise float fx = floor(u * (float)w), fy = floor(v * (float)h);
    int x = fx < 0.0f ? 0 : (fx > (float)(w - 1) ? (int)(w - 1) : (int)fx);
    int y = fy < 0.0f ? 0 : (fy > (float)(h - 1) ? (int)(h - 1) : (int)fy);
    return t.Load(int3(x, y, 0));
}
float4 BilinearRead(Texture2D<float4> t, float u, float v)
{
    uint w, h; t.GetDimensions(w, h);
    precise float fx = u * (float)w - 0.5f, fy = v * (float)h - 0.5f;
    precise float x0 = floor(fx), y0 = floor(fy);
    precise float ax = fx - x0, ay = fy - y0;
    int xa = x0 < 0.0f ? 0 : (x0 > (float)(w - 1) ? (int)(w - 1) : (int)x0);
    int xb = x0 + 1.0f < 0.0f ? 0 : (x0 + 1.0f > (float)(w - 1) ? (int)(w - 1) : (int)(x0 + 1.0f));
    int ya = y0 < 0.0f ? 0 : (y0 > (float)(h - 1) ? (int)(h - 1) : (int)y0);
    int yb = y0 + 1.0f < 0.0f ? 0 : (y0 + 1.0f > (float)(h - 1) ? (int)(h - 1) : (int)(y0 + 1.0f));
    float4 p00 = t.Load(int3(xa, ya, 0)), p10 = t.Load(int3(xb, ya, 0)), p01 = t.Load(int3(xa, yb, 0)), p11 = t.Load(int3(xb, yb, 0));
    precise float bx = 1.0f - ax, by = 1.0f - ay;
    precise float4 bottom = p00 * bx + p10 * ax;
    precise float4 top = p01 * bx + p11 * ax;
    precise float4 c = bottom * by + top * ay;
    return c;
}
";

    private const string Kernel = @"
cbuffer Cb0Buf : register(b0) { float4 cb0[138]; };
cbuffer Cb1Buf : register(b1) { float4 cb1[5]; };
Texture2D<float4> Art : register(t0);
Texture2D<float4> Mask : register(t1);
Texture2D<float4> Background : register(t2);
StructuredBuffer<float4> Inputs : register(t3);
RWStructuredBuffer<float4> Output : register(u0);
RWStructuredBuffer<uint> Discards : register(u1);
cbuffer Count : register(b2) { uint Pixels; uint3 Pad; };
" + Reads + @"
float4 S0(float u, float v) { return float4(1.0f, 1.0f, 1.0f, 1.0f); }
float4 S1(float u, float v) { return PointRead(Art, u, v); }
float4 S2(float u, float v) { return PointRead(Mask, u, v); }
float4 S3(float u, float v) { return BilinearRead(Background, u, v); }
#include ""GameBustProgram""
[numthreads(64, 1, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    uint i = id.x;
    if (i >= Pixels) return;
    float4 a = Inputs[i * 3], b = Inputs[i * 3 + 1], c = Inputs[i * 3 + 2];
    GameInputs v;
    v.v1x = a.x; v.v1y = a.y; v.v1z = a.z; v.v1w = a.w;
    v.v2x = b.x; v.v2y = b.y; v.v2z = b.z; v.v2w = b.w;
    v.v3x = c.x; v.v3y = c.y; v.v3z = c.z; v.v3w = c.w;
    GameRegisters r = (GameRegisters)0;
    Program(r, v);
    Output[i] = float4(r.o0x, r.o0y, r.o0z, r.o0w);
    Discards[i] = r.Discarded ? 1u : 0u;
}";

    internal static string Source(string kernel) => kernel.Replace("#include \"GameBustProgram\"", GpuPrograms.Source("GameBustProgram"));

    private static (float[] colour, uint[] discarded) RunOnCard(GpuDevice gpu, FakeGiTests.Fixture f, float[] cb0, List<GameInputs> inputs)
    {
        lock (gpu.Gate)
        {
            var ctx = gpu.Context;
            using var art = GpuTexture.Create<float>(gpu, f.ArtSize, f.ArtSize, Format.R32G32B32A32_Float, false, f.Art);
            using var mask = GpuTexture.Create<float>(gpu, f.ArtSize, f.ArtSize, Format.R32G32B32A32_Float, false, f.Mask);
            using var bg = GpuTexture.Create<float>(gpu, f.BgW, f.BgH, Format.R32G32B32A32_Float, false, f.Background);
            using var c0 = new GpuConstants(gpu, cb0.Length); c0.Upload(gpu, cb0);
            using var c1 = new GpuConstants(gpu, f.Cb1.Length); c1.Upload(gpu, f.Cb1);
            int n = inputs.Count;
            using var count = new GpuConstants(gpu, 4); count.Upload(gpu, new[] { BitConverter.Int32BitsToSingle(n) });
            var flat = new float[n * 12];
            for (int i = 0; i < n; i++)
            {
                var v = inputs[i];
                new[] { v.v1x, v.v1y, v.v1z, v.v1w, v.v2x, v.v2y, v.v2z, v.v2w, v.v3x, v.v3y, v.v3z, v.v3w }.CopyTo(flat, i * 12);
            }
            using var inBuf = Structured(gpu, flat, 16, n * 3);
            using var inView = gpu.Device.CreateShaderResourceView(inBuf);
            using var outBuf = gpu.Device.CreateBuffer(new BufferDescription((uint)(n * 16), BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
            using var outView = gpu.Device.CreateUnorderedAccessView(outBuf);
            using var discBuf = gpu.Device.CreateBuffer(new BufferDescription((uint)(n * 4), BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
            using var discView = gpu.Device.CreateUnorderedAccessView(discBuf);

            ctx.CSSetShader(gpu.Kernel(Source(Kernel), "Main", "GpuProgramTests"));
            ctx.CSSetConstantBuffer(0, c0.Buffer); ctx.CSSetConstantBuffer(1, c1.Buffer); ctx.CSSetConstantBuffer(2, count.Buffer);
            ctx.CSSetShaderResource(0, art.Read); ctx.CSSetShaderResource(1, mask.Read); ctx.CSSetShaderResource(2, bg.Read); ctx.CSSetShaderResource(3, inView);
            ctx.CSSetUnorderedAccessView(0, outView); ctx.CSSetUnorderedAccessView(1, discView);
            ctx.Dispatch((uint)((n + 63) / 64), 1, 1);
            ctx.CSSetUnorderedAccessView(0, null); ctx.CSSetUnorderedAccessView(1, null);
            return (ReadBack<float>(gpu, outBuf, n * 4), ReadBack<uint>(gpu, discBuf, n));
        }
    }

    internal static ID3D11Buffer Structured(GpuDevice gpu, float[] data, int stride, int count)
    {
        unsafe
        {
            fixed (float* p = data)
                return gpu.Device.CreateBuffer(new BufferDescription((uint)(stride * count), BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None,
                                                                     ResourceOptionFlags.BufferStructured, (uint)stride), new SubresourceData((IntPtr)p));
        }
    }

    internal static T[] ReadBack<T>(GpuDevice gpu, ID3D11Buffer buffer, int count) where T : unmanaged
    {
        var desc = buffer.Description;
        using var staging = gpu.Device.CreateBuffer(new BufferDescription(desc.ByteWidth, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        gpu.Context.CopyResource(staging, buffer);
        var map = gpu.Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var result = new T[count];
            unsafe { new ReadOnlySpan<T>((void*)map.DataPointer, count).CopyTo(result); }
            return result;
        }
        finally { gpu.Context.Unmap(staging, 0); }
    }

    [Fact]
    public void TheGamesBustProgramOnTheCardDrawsEveryFixturePixelAsTheGamesProgramDoes()
    {
        var gpu = GpuDevice.Shared;
        Assert.True(gpu != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var f = FakeGiTests.Fixture.Load();
        int pixels = 0, drawnWrong = 0, exact = 0, overHalfLevel = 0, offOver = 0;
        double worst = 0;
        foreach (var c in f.Cases)
        {
            var cb0 = f.Cb0At(c.Time);
            var cb0Off = (float[])cb0.Clone(); cb0Off[134 * 4] = 0f;   // the control: the fake GI off
            var inputs = c.Pixels.Select(p => FakeGiTests.InputsAt(f, c, p.X, p.Y)).ToList();
            var (colour, discarded) = RunOnCard(gpu!, f, cb0, inputs);
            var (offColour, _) = RunOnCard(gpu!, f, cb0Off, inputs);
            for (int i = 0; i < c.Pixels.Length; i++)
            {
                var p = c.Pixels[i];
                pixels++;
                if ((discarded[i] != 0) == p.Alive) { drawnWrong++; continue; }
                if (!p.Alive) { exact++; continue; }
                double d = Math.Max(Math.Max(Math.Abs(colour[i * 4] - p.R), Math.Abs(colour[i * 4 + 1] - p.G)),
                                    Math.Max(Math.Abs(colour[i * 4 + 2] - p.B), Math.Abs(colour[i * 4 + 3] - p.A)));
                worst = Math.Max(worst, d);
                if (d <= 1e-6) exact++;
                if (d * 255 > 0.5) overHalfLevel++;
                double off = Math.Max(Math.Max(Math.Abs(offColour[i * 4] - p.R), Math.Abs(offColour[i * 4 + 1] - p.G)), Math.Abs(offColour[i * 4 + 2] - p.B));
                if (off * 255 > 1) offOver++;
            }
        }
        _out.WriteLine($"the card ({gpu!.Adapter}): {exact} of {pixels} pixels within 1e-6 of the game's program, worst {worst:E2} ({worst * 255:0.000} levels), "
                       + $"{overHalfLevel} more than half a level off; drawn or not wrong at {drawnWrong}; with the fake GI off {offOver} drawn pixels over a level away");
        Assert.True(offOver > 2000, "control: the card's frame without the fake GI passes for the fixture");
        Assert.Equal(0, drawnWrong);
        Assert.Equal(0, overHalfLevel);
    }
}
