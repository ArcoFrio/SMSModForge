using System;
using System.Runtime.InteropServices;
using SMSModForge.Model;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SMSModForge.Rendering.Gpu;

/// <summary>
/// <see cref="NpcRender.Body"/> on the graphics card (1.9.1): the game's own bust
/// program - a pack NPC's material is a bust's - at every one of the game's
/// pixels in the frame (Programs/NpcBody.hlsl), the NPC's art over its rect,
/// lit by its layer's light texture, its edge glow reading the room. The body
/// and the blink (a sprite of its own through the same material) into buffers
/// the frame's passes read (<see cref="BodyRead"/>, <see cref="BlinkRead"/>).
/// One per preview.
/// </summary>
internal sealed class GpuNpcBody : IDisposable
{
    private const int N = BackdropRender.FrameSize;
    private readonly GpuDevice _gpu;
    private readonly ID3D11ComputeShader _kernel;
    private readonly GpuConstants _cb0, _cb1, _frame;
    private readonly ID3D11Buffer _body, _blink, _back;
    private readonly ID3D11UnorderedAccessView _bodyView, _blinkView;
    internal ID3D11ShaderResourceView BodyRead { get; }
    internal ID3D11ShaderResourceView BlinkRead { get; }
    private GpuTexture? _art, _mask, _blinkArt, _light, _background;
    private byte[]? _artOf, _blinkOf;
    private float[]? _decodeOf, _lightOf, _backgroundOf;
    private float[] _floats = Array.Empty<float>(), _maskFloats = Array.Empty<float>();

    private GpuNpcBody(GpuDevice gpu)
    {
        _gpu = gpu;
        _kernel = gpu.Kernel(Source(), "Main", "NpcBody");
        _cb0 = new GpuConstants(gpu, GameBustProgram.Cb0Size);
        _cb1 = new GpuConstants(gpu, GameBustProgram.Cb1Size);
        _frame = new GpuConstants(gpu, 16);
        (_body, _bodyView) = Output(gpu);
        (_blink, _blinkView) = Output(gpu);
        BodyRead = gpu.Device.CreateShaderResourceView(_body);
        BlinkRead = gpu.Device.CreateShaderResourceView(_blink);
        _back = gpu.Device.CreateBuffer(new BufferDescription((uint)(N * N * 16), BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
    }

    public static GpuNpcBody? TryCreate()
    {
        var gpu = GpuDevice.Shared;
        if (gpu == null) return null;
        lock (gpu.Gate) return new GpuNpcBody(gpu);
    }

    internal static string Source()
        => GpuPrograms.Source("NpcBody").Replace("#include \"GameBustProgram\"", GpuPrograms.Source("GameBustProgram"));   // English on purpose: HLSL source text, not words

    private static (ID3D11Buffer, ID3D11UnorderedAccessView) Output(GpuDevice gpu)
    {
        var buffer = gpu.NewBuffer(new BufferDescription((uint)(N * N * 16), BindFlags.UnorderedAccess | BindFlags.ShaderResource, ResourceUsage.Default,
                                                                   CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
        return (buffer, gpu.Device.CreateUnorderedAccessView(buffer));
    }

    /// <summary>Straight BGRA bytes (<paramref name="w"/> x <paramref name="h"/>, rows from the top)
    /// as the program reads them: RGBA floats, rows from the bottom, colour through <paramref name="lin"/> (or as stored).</summary>
    private static void AsRead(byte[] bgra, int w, int h, float[]? lin, float[] into)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = ((h - 1 - y) * w + x) * 4, o = (y * w + x) * 4;
                into[o] = lin != null ? lin[bgra[i + 2]] : bgra[i + 2] / 255f;
                into[o + 1] = lin != null ? lin[bgra[i + 1]] : bgra[i + 1] / 255f;
                into[o + 2] = lin != null ? lin[bgra[i]] : bgra[i] / 255f;
                into[o + 3] = bgra[i + 3] / 255f;
            }
    }

    private GpuTexture Texture(ref GpuTexture? t, int w, int h)
    {
        if (t == null || t.W != w || t.H != h) { t?.Dispose(); t = GpuTexture.Create<float>(_gpu, w, h, Format.R32G32B32A32_Float, false); }
        return t;
    }

    /// <summary>
    /// The NPC's body - and its blink when <paramref name="blinkTex"/> is given -
    /// into the card's buffers, for the same arguments as NpcRender.Body: its art
    /// and mask (<paramref name="w"/> x <paramref name="h"/>), its light (an
    /// NpcLight's texels), the edge glow with <paramref name="gi"/> over its room
    /// (<paramref name="giOn"/> false: none). The caller holds the device's gate.
    /// </summary>
    internal void Run(byte[] baseTex, byte[]? blinkTex, byte[] maskTex, int w, int h, JiggleParams p, (float r, float g, float b, float a) tint, float time,
                      FakeGi gi, bool giOn, NpcLight light, float left, float bottom, float width, float height,
                      int frameLeft, int frameBottom, float[]? decode, int zoom)
    {
        var ctx = _gpu.Context;
        var lin = decode ?? FakeGi.SrgbToLinear;
        if (_floats.Length != w * h * 4) { _floats = new float[w * h * 4]; _maskFloats = new float[w * h * 4]; _artOf = _blinkOf = null; }
        if (!ReferenceEquals(_artOf, baseTex) || !ReferenceEquals(_decodeOf, decode))
        {
            AsRead(baseTex, w, h, lin, _floats);
            Texture(ref _art, w, h).Upload<float>(_gpu, _floats);
            _artOf = baseTex; _decodeOf = decode; _blinkOf = null;
        }
        if (blinkTex != null && !ReferenceEquals(_blinkOf, blinkTex))
        {
            AsRead(blinkTex, w, h, lin, _floats);
            Texture(ref _blinkArt, w, h).Upload<float>(_gpu, _floats);
            _blinkOf = blinkTex;
        }
        // The mask is read every frame: the painter changes it in place.
        AsRead(maskTex, w, h, null, _maskFloats);
        Texture(ref _mask, w, h).Upload<float>(_gpu, _maskFloats);
        var (texels, tw, th) = light.Texels;
        if (!ReferenceEquals(_lightOf, texels))
        {
            var rgba = new float[tw * th * 4];
            for (int i = 0; i < tw * th; i++) { rgba[i * 4] = texels[i * 3]; rgba[i * 4 + 1] = texels[i * 3 + 1]; rgba[i * 4 + 2] = texels[i * 3 + 2]; rgba[i * 4 + 3] = 1f; }
            Texture(ref _light, tw, th).Upload<float>(_gpu, rgba);
            _lightOf = texels;
        }
        if (!ReferenceEquals(_backgroundOf, gi.Background))
        {
            Texture(ref _background, gi.BackgroundWidth, gi.BackgroundHeight).Upload<float>(_gpu, gi.Background);
            _backgroundOf = gi.Background;
        }

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
            I(frameLeft), I(frameBottom), I(zoom), 0f,
            (float)gi.ScreenWidth, (float)gi.ScreenHeight, left, bottom,
            width, height, 1f / (zoom * zoom), 0f,
            white ? 1f : Lin(tint.r), white ? 1f : Lin(tint.g), white ? 1f : Lin(tint.b), tint.a,
        });

        ctx.CSSetShader(_kernel);
        ctx.CSSetConstantBuffer(0, _cb0.Buffer); ctx.CSSetConstantBuffer(1, _cb1.Buffer); ctx.CSSetConstantBuffer(2, _frame.Buffer);
        ctx.CSSetShaderResource(1, _mask.Read); ctx.CSSetShaderResource(2, _background!.Read); ctx.CSSetShaderResource(3, _light!.Read);
        ctx.CSSetShaderResource(0, _art!.Read);
        ctx.CSSetUnorderedAccessView(0, _bodyView);
        ctx.Dispatch(N / 16, N / 16, 1);
        if (blinkTex != null)
        {
            ctx.CSSetShaderResource(0, _blinkArt!.Read);
            ctx.CSSetUnorderedAccessView(0, _blinkView);
            ctx.Dispatch(N / 16, N / 16, 1);
        }
        ctx.CSSetUnorderedAccessView(0, null);
        for (uint i = 0; i < 4; i++) ctx.CSSetShaderResource(i, null);
    }

    /// <summary>The body as drawn, read back - for tests.</summary>
    internal void ReadBody(float[] into)
    {
        lock (_gpu.Gate)
        {
            var ctx = _gpu.Context;
            ctx.CopyResource(_back, _body);
            var map = ctx.Map(_back, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try { Marshal.Copy(map.DataPointer, into, 0, N * N * 4); }
            finally { ctx.Unmap(_back, 0); }
        }
    }

    public void Dispose()
    {
        lock (_gpu.Gate)
        {
            _art?.Dispose(); _mask?.Dispose(); _blinkArt?.Dispose(); _light?.Dispose(); _background?.Dispose();
            BodyRead.Dispose(); BlinkRead.Dispose();
            _bodyView.Dispose(); _blinkView.Dispose(); _body.Dispose(); _blink.Dispose(); _back.Dispose();
            _cb0.Dispose(); _cb1.Dispose(); _frame.Dispose();
        }
    }
}
