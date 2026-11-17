using System;
using System.Runtime.InteropServices;
using SMSModForge.Model;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SMSModForge.Rendering.Gpu;

/// <summary>
/// <see cref="BackdropRender.Body"/> on the graphics card (1.9.1): the game's
/// own bust program at every one of the game's pixels in the frame
/// (Programs/BustBody.hlsl), into the same buffers, for the same arguments.
/// One per preview; its textures stay on the card while the art does.
/// </summary>
internal sealed class GpuBody : IDisposable
{
    private const int N = BackdropRender.FrameSize, S = JiggleShader.Size;
    private readonly GpuDevice _gpu;
    private readonly ID3D11ComputeShader _kernel;
    private readonly GpuConstants _cb0, _cb1, _frame;
    private readonly ID3D11Buffer _body, _silhouette;
    private readonly ID3D11UnorderedAccessView _bodyView, _silhouetteView;
    /// <summary>The body and the silhouette as the frame's passes read them, on the card.</summary>
    internal ID3D11ShaderResourceView BodyRead { get; }
    internal ID3D11ShaderResourceView SilhouetteRead { get; }
    private readonly ID3D11Buffer _bodyBack, _silhouetteBack;
    private GpuTexture? _art, _mask, _background;
    private byte[]? _artOf, _maskOf;
    private float[]? _decodeOf;
    private readonly float[] _artFloats = new float[S * S * 4], _maskFloats = new float[S * S * 4];

    private GpuBody(GpuDevice gpu)
    {
        _gpu = gpu;
        _kernel = gpu.Kernel(Source(), "Main", "BustBody");
        _cb0 = new GpuConstants(gpu, GameBustProgram.Cb0Size);
        _cb1 = new GpuConstants(gpu, GameBustProgram.Cb1Size);
        _frame = new GpuConstants(gpu, 16);
        (_body, _bodyView) = Output(gpu);
        (_silhouette, _silhouetteView) = Output(gpu);
        BodyRead = gpu.Device.CreateShaderResourceView(_body);
        SilhouetteRead = gpu.Device.CreateShaderResourceView(_silhouette);
        _bodyBack = Staging(gpu);
        _silhouetteBack = Staging(gpu);
    }

    /// <summary>One for a preview, or null when the PC has no card to draw on.</summary>
    public static GpuBody? TryCreate()
    {
        var gpu = GpuDevice.Shared;
        if (gpu == null) return null;
        lock (gpu.Gate) return new GpuBody(gpu);
    }

    internal static string Source()
        => GpuPrograms.Source("BustBody").Replace("#include \"GameBustProgram\"", GpuPrograms.Source("GameBustProgram"));   // English on purpose: HLSL source text, not words

    private static (ID3D11Buffer, ID3D11UnorderedAccessView) Output(GpuDevice gpu)
    {
        var buffer = gpu.NewBuffer(new BufferDescription((uint)(N * N * 16), BindFlags.UnorderedAccess | BindFlags.ShaderResource, ResourceUsage.Default,
                                                                   CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
        return (buffer, gpu.Device.CreateUnorderedAccessView(buffer));
    }

    private static ID3D11Buffer Staging(GpuDevice gpu)
        => gpu.Device.CreateBuffer(new BufferDescription((uint)(N * N * 16), BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));

    /// <summary>
    /// As <see cref="BackdropRender.Body"/>: the body (and the silhouette) of
    /// the bust in <paramref name="baseTex"/>, moved by <paramref name="maskTex"/>,
    /// over <paramref name="gi"/>'s backdrop (its background as it is now), into
    /// <paramref name="body"/> - with the fake GI's whole program whatever the
    /// quality (the card works it out exactly at no cost worth sparing).
    /// <paramref name="giOn"/> false draws it without the fake GI.
    /// </summary>
    public void Draw(byte[] baseTex, byte[] maskTex, JiggleParams p, (float r, float g, float b, float a) tint, float time,
                     FakeGi gi, bool giOn, int left, int frameBottom, int bustBottom,
                     float[] body, bool bodyShows, float[]? silhouetteOut, float silhouette, float[]? decode, int zoom)
    {
        bool withSilhouette = silhouetteOut != null && silhouette > 0f;
        lock (_gpu.Gate)
        {
            _gpu.Clean();
            if (_background == null || _background.W != gi.BackgroundWidth || _background.H != gi.BackgroundHeight)
            {
                _background?.Dispose();
                _background = GpuTexture.Create<float>(_gpu, gi.BackgroundWidth, gi.BackgroundHeight, Format.R32G32B32A32_Float, false);
            }
            _background.Upload<float>(_gpu, gi.Background);
            Run(baseTex, maskTex, p, tint, time, gi, giOn, left, frameBottom, bustBottom, bodyShows, withSilhouette, silhouette, decode, zoom, _background.Read);
            Read(_body, _bodyBack, body);
            if (withSilhouette) Read(_silhouette, _silhouetteBack, silhouetteOut!);
        }
    }

    /// <summary>The body (and the silhouette) into the card's buffers
    /// (<see cref="BodyRead"/>), the fake GI reading <paramref name="background"/>
    /// there. The caller holds the device's gate.</summary>
    internal void Run(byte[] baseTex, byte[] maskTex, JiggleParams p, (float r, float g, float b, float a) tint, float time,
                      FakeGi gi, bool giOn, int left, int frameBottom, int bustBottom, bool bodyShows, bool withSilhouette, float silhouette,
                      float[]? decode, int zoom, ID3D11ShaderResourceView background)
    {
        {
            var ctx = _gpu.Context;
            // The art as the program reads it: rows from the bottom, colour decoded as the game's card decodes it.
            if (!ReferenceEquals(_artOf, baseTex) || !ReferenceEquals(_decodeOf, decode))
            {
                var lin = decode ?? FakeGi.SrgbToLinear;
                AsRead(baseTex, lin, _artFloats);
                _art ??= GpuTexture.Create<float>(_gpu, S, S, Format.R32G32B32A32_Float, false);
                _art.Upload<float>(_gpu, _artFloats);
                _artOf = baseTex; _decodeOf = decode;
            }
            // The mask is read every frame: the painter changes it in place.
            AsRead(maskTex, null, _maskFloats);
            _mask ??= GpuTexture.Create<float>(_gpu, S, S, Format.R32G32B32A32_Float, false);
            _mask.Upload<float>(_gpu, _maskFloats);
            var cb0 = (float[])gi.Cb0.Clone();
            if (!giOn) cb0[134 * 4] = 0f;
            cb0[15 * 4] = time / 20; cb0[15 * 4 + 1] = time; cb0[15 * 4 + 2] = time * 2; cb0[15 * 4 + 3] = time * 3;
            _cb0.Upload(_gpu, cb0);
            var cb1 = new float[GameBustProgram.Cb1Size];
            cb1[0] = 1; cb1[1] = 1;
            for (int k = 4; k < 12; k++) cb1[k] = 1;
            cb1[12] = p.Speed; cb1[13] = p.Strength; cb1[14] = p.Frequency; cb1[15] = p.NoiseScale; cb1[16] = p.NoiseSpeed; cb1[17] = p.NoiseStrength;
            _cb1.Upload(_gpu, cb1);

            bool white = tint.r == 1f && tint.g == 1f && tint.b == 1f;
            static float Lin(float c) => (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
            float I(int v) => BitConverter.Int32BitsToSingle(v);
            _frame.Upload(_gpu, new[]
            {
                I(left), I(frameBottom), I(bustBottom), I(zoom),
                (float)gi.ScreenWidth, (float)gi.ScreenHeight, silhouette, I(bodyShows ? 1 : 0),
                white ? 1f : Lin(tint.r), white ? 1f : Lin(tint.g), white ? 1f : Lin(tint.b), tint.a,
                1f / (zoom * zoom), I(withSilhouette ? 1 : 0), 0f, 0f,
            });

            ctx.CSSetShader(_kernel);
            ctx.CSSetConstantBuffer(0, _cb0.Buffer); ctx.CSSetConstantBuffer(1, _cb1.Buffer); ctx.CSSetConstantBuffer(2, _frame.Buffer);
            ctx.CSSetShaderResource(0, _art!.Read); ctx.CSSetShaderResource(1, _mask.Read); ctx.CSSetShaderResource(2, background);
            ctx.CSSetUnorderedAccessView(0, _bodyView); ctx.CSSetUnorderedAccessView(1, _silhouetteView);
            ctx.Dispatch(N / 16, N / 16, 1);
            ctx.CSSetUnorderedAccessView(0, null); ctx.CSSetUnorderedAccessView(1, null);
            ctx.CSSetShaderResource(2, null);
        }
    }

    /// <summary>Straight BGRA bytes (rows from the top) as the program reads
    /// them: RGBA floats, rows from the bottom, colour through <paramref name="lin"/> (or as stored).</summary>
    private static void AsRead(byte[] bgra, float[]? lin, float[] into)
    {
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int i = ((S - 1 - y) * S + x) * 4, o = (y * S + x) * 4;
                into[o] = lin != null ? lin[bgra[i + 2]] : bgra[i + 2] / 255f;
                into[o + 1] = lin != null ? lin[bgra[i + 1]] : bgra[i + 1] / 255f;
                into[o + 2] = lin != null ? lin[bgra[i]] : bgra[i] / 255f;
                into[o + 3] = bgra[i + 3] / 255f;
            }
    }

    private void Read(ID3D11Buffer buffer, ID3D11Buffer staging, float[] into)
    {
        var ctx = _gpu.Context;
        ctx.CopyResource(staging, buffer);
        var map = ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try { Marshal.Copy(map.DataPointer, into, 0, N * N * 4); }
        finally { ctx.Unmap(staging, 0); }
    }

    public void Dispose()
    {
        lock (_gpu.Gate)
        {
            _art?.Dispose(); _mask?.Dispose(); _background?.Dispose();
            BodyRead.Dispose(); SilhouetteRead.Dispose();
            _bodyView.Dispose(); _silhouetteView.Dispose(); _body.Dispose(); _silhouette.Dispose();
            _bodyBack.Dispose(); _silhouetteBack.Dispose();
            _cb0.Dispose(); _cb1.Dispose(); _frame.Dispose();
        }
    }
}
