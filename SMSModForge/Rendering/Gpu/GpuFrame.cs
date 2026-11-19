using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SMSModForge.Model;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SMSModForge.Rendering.Gpu;

/// <summary>
/// The bust preview's whole frame over a backdrop with a lamp on the graphics
/// card (1.9.1): what JigglePreview does on the processor after the shadow -
/// the room lit through it, the edge glow's background, the body
/// (<see cref="GpuBody"/>), its faces and the intro's silhouette over it, the
/// lamp's glow, and the post-processing (<see cref="GpuPost"/>) - with only the
/// shadow texture going to the card and the preview's bytes coming back
/// (Programs/Frame.hlsl). One per preview.
/// </summary>
internal sealed class GpuFrame : IDisposable
{
    private const int N = BackdropRender.FrameSize, S = JiggleShader.Size;
    private readonly GpuDevice _gpu;
    private readonly ID3D11ComputeShader _light, _roomPass, _background, _clear, _layer, _overlay, _glow, _copy, _lampsAdd;
    private readonly GpuConstants _frameCb;
    public GpuBody Body { get; }
    public GpuPost Post { get; }

    private GameLamp? _lampOf;
    // A backdrop without a lamp: its room as the frame starts, what its lights add.
    private BackdropScene? _sceneOf;
    private ID3D11Buffer? _template, _lightFrame;
    private ID3D11ShaderResourceView? _lightFrameRead;
    private int _w, _h, _tw, _th, _bw, _bh;
    private ID3D11Buffer? _shadow, _lampTexels, _albedo, _noGlow, _room, _glowRgb, _texels, _target, _coverage;
    private ID3D11ShaderResourceView? _shadowRead, _lampTexelsRead, _albedoRead, _noGlowRead, _roomRead, _glowRead;
    private ID3D11UnorderedAccessView? _texelsWrite, _targetWrite, _coverageWrite;
    private GpuTexture? _backgroundTex, _frameOut;
    private readonly Dictionary<byte[], GpuTexture> _faces = new(ReferenceEqualityComparer.Instance);
    private float[]? _facesDecode;

    private GpuFrame(GpuDevice gpu, GpuBody body, GpuPost post)
    {
        _gpu = gpu; Body = body; Post = post;
        string source = GpuPrograms.Source("Frame");
        _light = gpu.Kernel(source, "LightTexture", "Frame");
        _roomPass = gpu.Kernel(source, "RoomPass", "Frame");
        _background = gpu.Kernel(source, "BackgroundPass", "Frame");
        _clear = gpu.Kernel(source, "ClearCoverage", "Frame");
        _layer = gpu.Kernel(source, "LayerPass", "Frame");
        _overlay = gpu.Kernel(source, "OverlayPass", "Frame");
        _glow = gpu.Kernel(source, "GlowPass", "Frame");
        _copy = gpu.Kernel(source, "CopyOut", "Frame");
        _lampsAdd = gpu.Kernel(source, "LampsAddPass", "Frame");
        _frameCb = new GpuConstants(gpu, 16);
    }

    /// <summary>One for a preview, or null when the PC has no card to draw on.</summary>
    public static GpuFrame? TryCreate()
    {
        var gpu = GpuDevice.Shared;
        if (gpu == null) return null;
        var body = GpuBody.TryCreate();
        var post = GpuPost.TryCreate();
        if (body == null || post == null) { body?.Dispose(); post?.Dispose(); return null; }
        lock (gpu.Gate) return new GpuFrame(gpu, body, post);
    }

    private ID3D11Buffer Floats(float[] data, bool writable, out ID3D11ShaderResourceView read)
    {
        unsafe
        {
            fixed (float* p = data)
            {
                var buffer = _gpu.Device.CreateBuffer(new BufferDescription((uint)(data.Length * 4), BindFlags.ShaderResource | (writable ? BindFlags.UnorderedAccess : BindFlags.None),
                                                                            ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4),
                                                      new SubresourceData((IntPtr)p));
                read = _gpu.Device.CreateShaderResourceView(buffer);
                return buffer;
            }
        }
    }

    private ID3D11Buffer Writable(int count, int stride, out ID3D11UnorderedAccessView write)
    {
        var buffer = _gpu.NewBuffer(new BufferDescription((uint)(count * stride), BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                                                    ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, (uint)stride));
        write = _gpu.Device.CreateUnorderedAccessView(buffer);
        return buffer;
    }

    /// <summary>The lamp's room on the card, once per backdrop.</summary>
    private void Prepare(GameLamp lamp, BackdropScene scene)
    {
        if (ReferenceEquals(_lampOf, lamp) && _sceneOf == null) return;
        ReleaseScene();
        _w = lamp.ScreenWidth; _h = lamp.ScreenHeight; _tw = lamp.Shadow.W; _th = lamp.Shadow.H;
        _bw = scene.Backdrop.BackgroundWidth; _bh = scene.Backdrop.BackgroundHeight;
        _shadow = Floats(new float[_tw * _th], false, out var shadowRead); _shadowRead = shadowRead;
        _lampTexels = Floats(lamp.LampTexels, false, out var lt); _lampTexelsRead = lt;
        _albedo = Floats(lamp.AlbedoRgb, false, out var al); _albedoRead = al;
        _noGlow = Floats(lamp.NoGlowRgb, false, out var ng); _noGlowRead = ng;
        _room = Floats(lamp.RoomRgb, false, out var rm); _roomRead = rm;
        _glowRgb = Floats(lamp.GlowRgb, false, out var gl); _glowRead = gl;
        _texels = Writable(_tw * _th, 16, out var tx); _texelsWrite = tx;
        _target = Writable(_w * _h, 16, out var tg); _targetWrite = tg;
        _coverage = Writable(N * N, 4, out var cv); _coverageWrite = cv;
        _backgroundTex = GpuTexture.Create<float>(_gpu, _bw, _bh, Format.R32G32B32A32_Float, true);
        _frameOut = GpuTexture.Create<float>(_gpu, _w, _h, Format.R32G32B32A32_Float, true);
        _lampOf = lamp;
    }

    /// <summary>A face (straight sRGB BGRA, rows from the top) as the overlay pass reads it, kept while the picture is.</summary>
    private GpuTexture Face(byte[] straight, float[] decode)
    {
        if (!ReferenceEquals(_facesDecode, decode)) { foreach (var f in _faces.Values) f.Dispose(); _faces.Clear(); _facesDecode = decode; }
        if (_faces.TryGetValue(straight, out var t)) return t;
        if (_faces.Count > 32) { foreach (var f in _faces.Values) f.Dispose(); _faces.Clear(); }
        var px = new float[S * S * 4];
        for (int i = 0; i < S * S; i++)
        {
            px[i * 4] = decode[straight[i * 4 + 2]]; px[i * 4 + 1] = decode[straight[i * 4 + 1]]; px[i * 4 + 2] = decode[straight[i * 4]];
            px[i * 4 + 3] = straight[i * 4 + 3] / 255f;
        }
        t = GpuTexture.Create<float>(_gpu, S, S, Format.R32G32B32A32_Float, false, px);
        _faces[straight] = t;
        return t;
    }

    private void Run(ID3D11ComputeShader kernel, int x, int y)
    {
        var ctx = _gpu.Context;
        ctx.CSSetShader(kernel);
        ctx.Dispatch((uint)x, (uint)y, 1);
    }

    private void UnbindAll()
    {
        var ctx = _gpu.Context;
        for (uint i = 0; i < 9; i++) ctx.CSSetShaderResource(i, null);
        for (uint i = 0; i < 5; i++) ctx.CSSetUnorderedAccessView(i, null);
    }

    /// <summary>
    /// The preview's frame: as JigglePreview's lamp path from the room on -
    /// <paramref name="shadow"/> the lamp's shadow texture the processor drew
    /// (BustShadow.Draw) - into <paramref name="output"/> (the screen's BGRA).
    /// <paramref name="giOn"/> the quality's edge glow.
    /// </summary>
    public void Draw(GameLamp lamp, float[] shadow, BackdropScene scene, byte[] baseTex, byte[] maskTex, JiggleParams p,
                     (float r, float g, float b, float a) tint, float time, bool giOn, int bustBottom, bool bodyShows, float silhouette,
                     byte[]? expression, byte[]? mouth, byte[]? blink, BackdropRender.PostParts parts, int zoom, byte[] output)
    {
        lock (_gpu.Gate)
        {
            var ctx = _gpu.Clean();
            Prepare(lamp, scene);
            unsafe
            {
                fixed (float* s = shadow) ctx.UpdateSubresource(_shadow!, 0, null, (IntPtr)s, 0, 0);
            }
            _shadowZero = false;
            float I(int v) => BitConverter.Int32BitsToSingle(v);
            var (gr, gg, gb) = lamp.GlobalLight;
            _frameCb.Upload(_gpu, new[]
            {
                I(_w), I(_h), I(_tw), I(_th), I(scene.Left), I(scene.FrameBottom), I(bustBottom), I(1),
                gr, gg, gb, lamp.Intensity, lamp.VolumeIntensity, I(_bw), I(_bh), I(lamp.GlowWithTheRoom ? 1 : 0),
            });
            ctx.CSSetConstantBuffer(0, _frameCb.Buffer);
            ctx.CSSetShaderResource(0, _shadowRead!); ctx.CSSetShaderResource(1, _lampTexelsRead!); ctx.CSSetShaderResource(2, _albedoRead!);
            ctx.CSSetShaderResource(3, _noGlowRead!); ctx.CSSetShaderResource(4, _roomRead!); ctx.CSSetShaderResource(5, _glowRead!);
            ctx.CSSetUnorderedAccessView(0, _texelsWrite!); ctx.CSSetUnorderedAccessView(1, _targetWrite!);
            ctx.CSSetUnorderedAccessView(2, _backgroundTex!.Write!); ctx.CSSetUnorderedAccessView(3, _coverageWrite!);
            ctx.CSSetUnorderedAccessView(4, _frameOut!.Write!);

            // The room through the shadow, and the edge glow's background from it.
            Run(_light, (_tw * _th + 63) / 64, 1);
            Run(_roomPass, (_w + 15) / 16, (_h + 15) / 16);
            Run(_background, (_bw + 15) / 16, (_bh + 15) / 16);
            Run(_clear, N / 16, N / 16);
            UnbindAll();

            // The body, its edge glow reading that background.
            bool withSilhouette = silhouette > 0f;
            Body.Run(baseTex, maskTex, p, tint, time, scene.Gi, giOn, scene.Left, scene.FrameBottom, bustBottom, bodyShows, withSilhouette, silhouette,
                     scene.Decode, zoom, _backgroundTex.Read);
            UnbindAll();

            // Into the camera's target as the game draws them: body, faces, silhouette, then the glow.
            void Rebind()
            {
                ctx.CSSetConstantBuffer(0, _frameCb.Buffer);
                ctx.CSSetShaderResource(0, _shadowRead!); ctx.CSSetShaderResource(4, _roomRead!); ctx.CSSetShaderResource(5, _glowRead!);
                ctx.CSSetUnorderedAccessView(1, _targetWrite!); ctx.CSSetUnorderedAccessView(3, _coverageWrite!);
                ctx.CSSetUnorderedAccessView(4, _frameOut!.Write!);
            }
            Rebind();
            ctx.CSSetShaderResource(6, Body.BodyRead);
            Run(_layer, N / 16, N / 16);
            foreach (var face in new[] { expression, mouth, blink })
            {
                if (face == null) continue;
                ctx.CSSetShaderResource(7, Face(face, scene.Decode).Read);
                Run(_overlay, N / 16, N / 16);
            }
            if (withSilhouette)
            {
                ctx.CSSetShaderResource(6, Body.SilhouetteRead);
                Run(_layer, N / 16, N / 16);
            }
            ctx.CSSetShaderResource(6, null);
            Run(_glow, (_w + 15) / 16, (_h + 15) / 16);
            Run(_copy, (_w + 15) / 16, (_h + 15) / 16);
            UnbindAll();

            Post.PresentFrom(_frameOut, scene, parts, output);
        }
    }

    /// <summary>A target-shaped buffer holding <paramref name="data"/>, to copy
    /// the target from.</summary>
    private ID3D11Buffer WritableWith(float[] data)
    {
        unsafe
        {
            fixed (float* p = data)
                return _gpu.Device.CreateBuffer(new BufferDescription((uint)(data.Length * 4), BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                                                      ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16),
                                                new SubresourceData((IntPtr)p));
        }
    }

    /// <summary>A backdrop without a lamp on the card, once per backdrop.</summary>
    private void PrepareNoLamp(BackdropScene scene)
    {
        if (ReferenceEquals(_sceneOf, scene)) return;
        ReleaseScene();
        var b = scene.Backdrop;
        _w = b.ScreenWidth; _h = b.ScreenHeight; _tw = 1; _th = 1;
        _bw = b.BackgroundWidth; _bh = b.BackgroundHeight;
        _template = WritableWith(scene.Template.Px);
        _target = Writable(_w * _h, 16, out var tg); _targetWrite = tg;
        _coverage = Writable(N * N, 4, out var cv); _coverageWrite = cv;
        _lightFrame = Floats(scene.LightFrame, false, out var lf); _lightFrameRead = lf;
        _backgroundTex = GpuTexture.Create<float>(_gpu, _bw, _bh, Format.R32G32B32A32_Float, false, b.Background);
        _frameOut = GpuTexture.Create<float>(_gpu, _w, _h, Format.R32G32B32A32_Float, true);
        _sceneOf = scene;
    }

    /// <summary>
    /// The preview's frame over a backdrop without a lamp: as JigglePreview's
    /// path there - the room, the body with its edge glow from the backdrop's
    /// background, the faces and the silhouette, what the lights add where
    /// the bust covers, the post-processing - into <paramref name="output"/>.
    /// </summary>
    public void DrawNoLamp(BackdropScene scene, byte[] baseTex, byte[] maskTex, JiggleParams p,
                           (float r, float g, float b, float a) tint, float time, bool giOn, int bustBottom, bool bodyShows, float silhouette,
                           byte[]? expression, byte[]? mouth, byte[]? blink, BackdropRender.PostParts parts, int zoom, byte[] output)
    {
        lock (_gpu.Gate)
        {
            var ctx = _gpu.Clean();
            PrepareNoLamp(scene);
            ctx.CopyResource(_target!, _template!);
            float I(int v) => BitConverter.Int32BitsToSingle(v);
            _frameCb.Upload(_gpu, new[]
            {
                I(_w), I(_h), I(_tw), I(_th), I(scene.Left), I(scene.FrameBottom), I(bustBottom), I(0),
                1f, 1f, 1f, 0f, 0f, I(_bw), I(_bh), 0f,
            });
            bool withSilhouette = silhouette > 0f;
            Body.Run(baseTex, maskTex, p, tint, time, scene.Gi, giOn, scene.Left, scene.FrameBottom, bustBottom, bodyShows, withSilhouette, silhouette,
                     scene.Decode, zoom, _backgroundTex!.Read);
            UnbindAll();
            ctx.CSSetConstantBuffer(0, _frameCb.Buffer);
            ctx.CSSetUnorderedAccessView(1, _targetWrite!); ctx.CSSetUnorderedAccessView(3, _coverageWrite!);
            ctx.CSSetUnorderedAccessView(4, _frameOut!.Write!);
            Run(_clear, N / 16, N / 16);
            ctx.CSSetShaderResource(6, Body.BodyRead);
            Run(_layer, N / 16, N / 16);
            foreach (var face in new[] { expression, mouth, blink })
            {
                if (face == null) continue;
                ctx.CSSetShaderResource(7, Face(face, scene.Decode).Read);
                Run(_overlay, N / 16, N / 16);
            }
            if (withSilhouette)
            {
                ctx.CSSetShaderResource(6, Body.SilhouetteRead);
                Run(_layer, N / 16, N / 16);
            }
            ctx.CSSetShaderResource(6, null);
            ctx.CSSetShaderResource(8, _lightFrameRead!);
            Run(_lampsAdd, N / 16, N / 16);
            Run(_copy, (_w + 15) / 16, (_h + 15) / 16);
            UnbindAll();
            Post.PresentFrom(_frameOut, scene, parts, output);
        }
    }

    private GpuNpcBody? _npc;
    private bool _shadowZero;

    /// <summary>
    /// The NPC preview's frame over a backdrop on the card (1.9.1): as
    /// NpcRender.Compose on the processor - the room (a lamp's with no bust's
    /// shadow), the NPC's body and its blink through the bust's program at their
    /// rect (GpuNpcBody), a lamp's glow or what the place's lights add, the
    /// post-processing - into <paramref name="output"/>.
    /// </summary>
    public void DrawNpc(BackdropScene scene, FakeGi gi, bool giOn, NpcLight light, byte[] baseTex, byte[]? blinkTex, byte[] maskTex, int w, int h,
                        JiggleParams p, (float r, float g, float b, float a) tint, float time,
                        float left, float bottom, float width, float height, BackdropRender.PostParts parts, int zoom, byte[] output)
    {
        lock (_gpu.Gate)
        {
            var ctx = _gpu.Clean();
            _npc ??= GpuNpcBody.TryCreate() ?? throw new InvalidOperationException("no graphics card device");
            float I(int v) => BitConverter.Int32BitsToSingle(v);
            var lamp = scene.Lamp;
            if (lamp != null)
            {
                Prepare(lamp, scene);
                if (!_shadowZero)
                {
                    var zero = new float[_tw * _th];
                    unsafe { fixed (float* s = zero) ctx.UpdateSubresource(_shadow!, 0, null, (IntPtr)s, 0, 0); }
                    _shadowZero = true;
                }
                var (gr, gg, gb) = lamp.GlobalLight;
                _frameCb.Upload(_gpu, new[]
                {
                    I(_w), I(_h), I(_tw), I(_th), I(scene.Left), I(scene.FrameBottom), I(0), I(1),
                    // the NPC stands on the room's layer: under the lamp's glow however it is drawn
                    gr, gg, gb, lamp.Intensity, lamp.VolumeIntensity, I(_bw), I(_bh), I((lamp.GlowWithTheRoom ? 1 : 0) | 2),
                });
                ctx.CSSetConstantBuffer(0, _frameCb.Buffer);
                ctx.CSSetShaderResource(0, _shadowRead!); ctx.CSSetShaderResource(1, _lampTexelsRead!); ctx.CSSetShaderResource(2, _albedoRead!);
                ctx.CSSetShaderResource(3, _noGlowRead!); ctx.CSSetShaderResource(4, _roomRead!); ctx.CSSetShaderResource(5, _glowRead!);
                ctx.CSSetUnorderedAccessView(0, _texelsWrite!); ctx.CSSetUnorderedAccessView(1, _targetWrite!);
                ctx.CSSetUnorderedAccessView(3, _coverageWrite!); ctx.CSSetUnorderedAccessView(4, _frameOut!.Write!);
                // The room with no bust's shadow.
                Run(_light, (_tw * _th + 63) / 64, 1);
                Run(_roomPass, (_w + 15) / 16, (_h + 15) / 16);
            }
            else
            {
                PrepareNoLamp(scene);
                ctx.CopyResource(_target!, _template!);
                _frameCb.Upload(_gpu, new[]
                {
                    I(_w), I(_h), I(_tw), I(_th), I(scene.Left), I(scene.FrameBottom), I(0), I(0),
                    1f, 1f, 1f, 0f, 0f, I(_bw), I(_bh), 0f,
                });
            }
            UnbindAll();
            _npc.Run(baseTex, blinkTex, maskTex, w, h, p, tint, time, gi, giOn, light, left, bottom, width, height,
                     scene.Left, scene.FrameBottom, scene.Decode, zoom);
            UnbindAll();
            // Into the camera's target: the body, the blink, then the lamp's glow or the lights.
            ctx.CSSetConstantBuffer(0, _frameCb.Buffer);
            if (lamp != null) { ctx.CSSetShaderResource(0, _shadowRead!); ctx.CSSetShaderResource(4, _roomRead!); ctx.CSSetShaderResource(5, _glowRead!); }
            ctx.CSSetUnorderedAccessView(1, _targetWrite!); ctx.CSSetUnorderedAccessView(3, _coverageWrite!);
            ctx.CSSetUnorderedAccessView(4, _frameOut!.Write!);
            Run(_clear, N / 16, N / 16);
            ctx.CSSetShaderResource(6, _npc.BodyRead);
            Run(_layer, N / 16, N / 16);
            if (blinkTex != null)
            {
                ctx.CSSetShaderResource(6, _npc.BlinkRead);
                Run(_layer, N / 16, N / 16);
            }
            ctx.CSSetShaderResource(6, null);
            if (lamp != null) Run(_glow, (_w + 15) / 16, (_h + 15) / 16);
            else
            {
                ctx.CSSetShaderResource(8, _lightFrameRead!);
                Run(_lampsAdd, N / 16, N / 16);
            }
            Run(_copy, (_w + 15) / 16, (_h + 15) / 16);
            UnbindAll();
            Post.PresentFrom(_frameOut, scene, parts, output);
        }
    }

    private void ReleaseScene()
    {
        _shadowZero = false;
        _template?.Dispose(); _lightFrameRead?.Dispose(); _lightFrame?.Dispose();
        _template = null; _lightFrameRead = null; _lightFrame = null; _sceneOf = null;
        _shadowRead?.Dispose(); _lampTexelsRead?.Dispose(); _albedoRead?.Dispose(); _noGlowRead?.Dispose(); _roomRead?.Dispose(); _glowRead?.Dispose();
        _texelsWrite?.Dispose(); _targetWrite?.Dispose(); _coverageWrite?.Dispose();
        _shadow?.Dispose(); _lampTexels?.Dispose(); _albedo?.Dispose(); _noGlow?.Dispose(); _room?.Dispose(); _glowRgb?.Dispose();
        _texels?.Dispose(); _target?.Dispose(); _coverage?.Dispose();
        _backgroundTex?.Dispose(); _frameOut?.Dispose();
        _lampOf = null;
    }

    public void Dispose()
    {
        lock (_gpu.Gate)
        {
            ReleaseScene();
            foreach (var f in _faces.Values) f.Dispose();
            _frameCb.Dispose();
            _npc?.Dispose();
        }
        Body.Dispose();
        Post.Dispose();
    }
}
