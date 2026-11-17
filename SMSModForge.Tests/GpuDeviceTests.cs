using System;
using System.IO;
using System.Linq;
using SMSModForge.Rendering.Gpu;
using Vortice.DXGI;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The graphics card the previews draw on (1.9.1): a kernel compiled by
/// Windows' own compiler runs on the card and its picture comes back to the
/// bit what the processor computes - IEEE single operations, each as written.
/// Controls: a kernel one operation different does not pass for it, a
/// program that cannot compile says why, and Windows' software device runs
/// the same kernel to the same bits.
/// </summary>
public sealed class GpuDeviceTests
{
    private readonly ITestOutputHelper _out;
    public GpuDeviceTests(ITestOutputHelper o) => _out = o;

    private const string Source = @"
RWTexture2D<float4> Output : register(u0);
cbuffer Settings : register(b0) { float Scale; float Offset; float2 Unused; };
[numthreads(8, 8, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    precise float x = (float)id.x, y = (float)id.y;
    precise float a = x * Scale + Offset;
    precise float b = sqrt(x + 1.0f) / (y + 3.0f);
    precise float c = frac(a * 0.37f);
    Output[id.xy] = float4(a, b, c, x * y);
}
void Other(uint3 id : SV_DispatchThreadID);
[numthreads(8, 8, 1)]
void Wrong(uint3 id : SV_DispatchThreadID)
{
    precise float x = (float)id.x, y = (float)id.y;
    precise float a = x * Scale + Offset;
    precise float b = sqrt(x + 1.0f) / (y + 3.0f);
    precise float c = frac(a * 0.371f);
    Output[id.xy] = float4(a, b, c, x * y);
}";

    private static float[] Cpu(int w, int h, float scale, float offset)
    {
        var px = new float[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                float fx = x, fy = y;
                float a = fx * scale + offset;
                float b = MathF.Sqrt(fx + 1f) / (fy + 3f);
                float t = a * 0.37f;
                float c = t - MathF.Floor(t);
                px[i] = a; px[i + 1] = b; px[i + 2] = c; px[i + 3] = fx * fy;
            }
        return px;
    }

    private static float[] Run(GpuDevice gpu, string entry, int w, int h, float scale, float offset)
    {
        lock (gpu.Gate)
        {
            using var output = GpuTexture.Create<float>(gpu, w, h, Format.R32G32B32A32_Float, writable: true);
            using var settings = new GpuConstants(gpu, 4);
            settings.Upload(gpu, new[] { scale, offset });
            var kernel = gpu.Kernel(Source, entry, "GpuDeviceTests");
            gpu.Context.CSSetShader(kernel);
            gpu.Context.CSSetConstantBuffer(0, settings.Buffer);
            gpu.Context.CSSetUnorderedAccessView(0, output.Write!);
            gpu.Context.Dispatch((uint)((w + 7) / 8), (uint)((h + 7) / 8), 1);
            gpu.Context.CSSetUnorderedAccessView(0, null);
            using var back = new GpuReadback(gpu, w, h, Format.R32G32B32A32_Float);
            var px = new float[w * h * 4];
            back.Read<float>(gpu, output, px);
            return px;
        }
    }

    private static int Differing(float[] a, float[] b)
        => a.Zip(b).Count(p => BitConverter.SingleToInt32Bits(p.First) != BitConverter.SingleToInt32Bits(p.Second));

    [Fact]
    public void AKernelOnTheCardComputesWhatTheProcessorDoes_ByDirect3DsRules()
    {
        var gpu = GpuDevice.Shared;
        Assert.True(gpu != null, "no graphics card device: " + GpuDevice.SharedFailure);
        _out.WriteLine($"the card: {gpu!.Adapter}");
        const int W = 200, H = 120;
        var cpu = Cpu(W, H, 1.37f, -3.5f);
        var card = Run(gpu, "Main", W, H, 1.37f, -3.5f);
        Hold(cpu, card, "the card");

        // The control: one constant different must show.
        var wrong = Run(gpu, "Wrong", W, H, 1.37f, -3.5f);
        int wrongOff = Differing(cpu, wrong);
        _out.WriteLine($"control: the other kernel differs at {wrongOff}");
        Assert.True(wrongOff > 1000, "control: a kernel computing something else passes for this one");

        // Windows' software device: the same, by the same rules.
        var warp = GpuDevice.TryCreate(software: true, out string? why);
        Assert.True(warp != null, "no software device: " + why);
        using (warp) Hold(cpu, Run(warp!, "Main", W, H, 1.37f, -3.5f), "the software device");
    }

    /// <summary>
    /// Direct3D's rules for a card's arithmetic (measured on the author's RTX
    /// 4070 Ti SUPER, 2026-11-16): add, multiply and frac are IEEE, to the bit;
    /// division and square root may be a few units in the last place off (the
    /// rules allow 2.5 for division) - the game's own frames have them too, as
    /// it draws on the card. Channel 1 is the division and square root.
    /// </summary>
    private void Hold(float[] cpu, float[] card, string what)
    {
        for (int c = 0; c < 4; c++)
        {
            int n = 0, worst = 0;
            for (int i = c; i < cpu.Length; i += 4)
            {
                int ulps = Math.Abs(BitConverter.SingleToInt32Bits(cpu[i]) - BitConverter.SingleToInt32Bits(card[i]));
                if (ulps != 0) { n++; worst = Math.Max(worst, ulps); }
            }
            _out.WriteLine($"{what}, channel {c}: {n} of {cpu.Length / 4} differ from the processor's, at most {worst} units in the last place");
            if (c == 1) Assert.True(worst <= 4, $"{what}: division and square root {worst} units off");
            else Assert.True(n == 0, $"{what}: channel {c} is not the processor's to the bit ({n} differ)");
        }
    }

    [Fact]
    public void AProgramThatCannotCompileSaysWhy_AndACompiledOneIsKept()
    {
        var e = Assert.Throws<InvalidOperationException>(() => GpuDevice.Bytecode("[numthreads(1,1,1)] void Main() { undefined_thing(); }", "Main", "GpuDeviceTestsBroken"));
        _out.WriteLine(e.Message);
        Assert.Contains("undefined_thing", e.Message);
        var first = GpuDevice.Bytecode(Source, "Main", "GpuDeviceTestsKept");
        var kept = Directory.GetFiles(Services.AppDataFolder.PathOf("ShaderCache"), "GpuDeviceTestsKept-Main-*.cso");
        Assert.Single(kept);
        Assert.Equal(first, File.ReadAllBytes(kept[0]));
    }
}
