using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace SMSModForge.Rendering.Gpu;

/// <summary>What a picture of the place's scene is drawn as.</summary>
internal enum PlaceItemKind : byte
{
    /// <summary>The picture itself.</summary>
    Picture,
    /// <summary>Its alpha filled with a colour (a tint, a shadow's disc, a marker's line).</summary>
    Tint,
}

/// <summary>How a picture is read where it is drawn smaller or larger than it is.</summary>
internal enum PlaceSampling : byte
{
    /// <summary>The nearest texel (WPF's NearestNeighbor: the level's art, its objects).</summary>
    Nearest,
    /// <summary>Between its four nearest (WPF's default).</summary>
    Linear,
    /// <summary>Between four of its smaller copies (WPF's HighQuality: an NPC's pose).</summary>
    Smooth,
}

/// <summary>A picture the card keeps itself, made here: a disc (a shadow, a marker's drop) or a plain fill.</summary>
internal enum PlaceBuiltIn : byte { None, Disc, Solid }

/// <summary>A shader's picture, drawn on the card and left there: where the scene reads it from, once drawn.</summary>
internal sealed class PlaceCardPicture
{
    /// <summary>Its bytes on the card (premultiplied BGRA, rows from the top) and their size, after
    /// <see cref="PlaceFrame.DrawFirst"/> has run; null when it could not be drawn.</summary>
    public Func<(ID3D11ShaderResourceView? Bytes, int W, int H)> Drawn = () => (null, 0, 0);
}

/// <summary>
/// One thing the place's world layer draws, as the card is to draw it: the
/// picture, where its unit square lands on the canvas (the game's 1920x1080
/// screen), how it is read and blended.
/// </summary>
internal struct PlaceItem
{
    /// <summary>A still picture (frozen: read on any thread).</summary>
    public BitmapSource? Bitmap;
    /// <summary>A picture's premultiplied BGRA, taken on the interface's thread (one that is not frozen).</summary>
    public byte[]? Pixels;
    public int PixelsW, PixelsH;
    /// <summary>A shader's picture, on the card.</summary>
    public PlaceCardPicture? OnCard;
    public PlaceBuiltIn BuiltIn;
    /// <summary>Canvas point = (u, v) through this: x = M11 u + M21 v + OffsetX, y = M12 u + M22 v + OffsetY.</summary>
    public double M11, M12, M21, M22, OffsetX, OffsetY;
    public float Opacity;
    public PlaceItemKind Kind;
    public PlaceSampling Sampling;
    /// <summary>In the lit mask: 0 not drawn, 1 lit, 2 drawn with the unlit material.</summary>
    public byte Lit;
    /// <summary>A tint's colour (the screen's bytes over 255, straight) and alpha.</summary>
    public float R, G, B, A;

    public readonly bool SameAs(in PlaceItem o)
        => ReferenceEquals(Bitmap, o.Bitmap) && ReferenceEquals(Pixels, o.Pixels) && ReferenceEquals(OnCard, o.OnCard) && BuiltIn == o.BuiltIn
           && M11 == o.M11 && M12 == o.M12 && M21 == o.M21 && M22 == o.M22 && OffsetX == o.OffsetX && OffsetY == o.OffsetY
           && Opacity == o.Opacity && Kind == o.Kind && Sampling == o.Sampling && Lit == o.Lit
           && R == o.R && G == o.G && B == o.B && A == o.A;
}

/// <summary>
/// A frame of the place preview for the card: the scene's pictures in their
/// order, the part of the canvas the view shows and at how many pixels, and
/// what of the game's look goes over it. Two frames that are
/// <see cref="SameAs"/> draw the same picture - which is how a still place is
/// known to be still, without drawing it.
/// </summary>
internal sealed class PlaceFrame
{
    public PlaceItem[] Items = new PlaceItem[64];
    public int Count;
    /// <summary>The shaders' pictures, drawn on the card before the scene reads them.</summary>
    public readonly List<Action> DrawFirst = new();
    /// <summary>What changes the pictures though the list stays the same: the shaders' clock.</summary>
    public long Stamp;
    public double ViewX, ViewY, ViewW, ViewH;
    public int Vw, Vh;
    public double CanvasW, CanvasH;
    /// <summary>Something in the scene is drawn with the unlit material: the lit mask is read.</summary>
    public bool Masked;
    public PlaceLights? Lights;
    public BackdropRender.PostParts Parts;
    public BackdropScene? Scene;
    /// <summary>The NPCs' wet drops (WetDrops): blended over the lit place, before the look.</summary>
    public WetDrops.Drop[] Drops = new WetDrops.Drop[16];
    public int DropCount;

    public void Clear()
    {
        Array.Clear(Items, 0, Count);
        Count = 0;
        DrawFirst.Clear();
        Stamp = 0;
        Masked = false;
        Lights = null;
        Scene = null;
        DropCount = 0;
    }

    public ref PlaceItem Add()
    {
        if (Count == Items.Length) Array.Resize(ref Items, Items.Length * 2);
        return ref Items[Count++];
    }

    public void AddDrop(in WetDrops.Drop d)
    {
        if (DropCount == Drops.Length) Array.Resize(ref Drops, Drops.Length * 2);
        Drops[DropCount++] = d;
    }

    /// <summary>The drops, as the processor's passes take them (a copy: the frame is used again).</summary>
    public WetDrops.Over? DropsOver(double canvasW, double canvasH)
    {
        var mips = WetDrops.Texture;
        if (DropCount == 0 || mips == null) return null;
        var copy = new WetDrops.Drop[DropCount];
        Array.Copy(Drops, copy, DropCount);
        return new WetDrops.Over(copy, DropCount, mips, canvasW, canvasH);
    }

    /// <param name="butStamp">Whatever the shaders' clock says.</param>
    public bool SameAs(PlaceFrame o, bool butStamp = false)
    {
        if (Count != o.Count || (!butStamp && Stamp != o.Stamp) || Vw != o.Vw || Vh != o.Vh || ViewX != o.ViewX || ViewY != o.ViewY || ViewW != o.ViewW || ViewH != o.ViewH
            || CanvasW != o.CanvasW || CanvasH != o.CanvasH || Masked != o.Masked || !ReferenceEquals(Lights, o.Lights) || Parts != o.Parts
            || !ReferenceEquals(Scene, o.Scene) || DropCount != o.DropCount) return false;
        for (int i = 0; i < Count; i++)
            if (!Items[i].SameAs(o.Items[i])) return false;
        for (int i = 0; i < DropCount; i++)
        {
            ref readonly var a = ref Drops[i];
            ref readonly var b = ref o.Drops[i];
            if (a.X != b.X || a.Y != b.Y || a.W != b.W || a.H != b.H || a.Alpha != b.Alpha) return false;
        }
        return true;
    }
}

/// <summary>
/// The place preview's scene drawn by the graphics card (1.10.0, the author:
/// "The Places preview is very laggy with all checkboxes enabled on ultra, even
/// though it's barely using my PC's resources at all"). Until then the look
/// over a place took the scene from WPF's software renderer on the interface's
/// thread - two to four pictures of the whole world layer a frame, ten
/// milliseconds and more each time - decoded and lit it on the processor, and
/// only then handed it to the card: thirty looks a second in a small view,
/// with the interface's thread held for a third of every frame.
/// <para/>
/// Here the card draws the scene itself: every picture of the world layer as
/// WPF draws it (premultiplied, in the screen's own bytes, in the scene's
/// order - Programs/PlaceScene.hlsl), at the view's pixels and, for the bloom,
/// the whole screen at the game's size; into the camera's format and through
/// the place's lights (Programs/PlaceLit.hlsl, <see cref="PlaceLights"/>
/// operation for operation); then the game's look as before
/// (<see cref="GpuPost"/>). The shaders' pictures are read where the card drew
/// them. One readback, of the view's bytes.
/// <para/>
/// The processor's way (PlacePreview.RenderStill, GamePost.LookOverView) stays
/// the reference - it is what the in-game suite holds to the game - and the
/// fallback; GpuPlaceSceneTests holds this to it.
/// </summary>
internal sealed class GpuPlaceScene : IDisposable
{
    private const Format Bytes = Format.B8G8R8A8_UNorm;
    private const int ScreenW = PlaceLights.ScreenW, ScreenH = PlaceLights.ScreenH;

    private readonly GpuDevice _gpu;
    private readonly ID3D11VertexShader _vertex;
    private readonly ID3D11PixelShader _pixel;
    private readonly ID3D11ComputeShader _lit;
    private readonly ID3D11BlendState _over;
    private readonly ID3D11RasterizerState _raster;
    private readonly ID3D11SamplerState _nearest, _linear, _smooth;
    private readonly GpuConstants _itemCb, _litCb;

    /// <summary>What the scene is drawn into: its colour and its lit mask.</summary>
    private sealed class Canvas : IDisposable
    {
        public ID3D11Texture2D Colour = null!, Lit = null!;
        public ID3D11RenderTargetView ColourTarget = null!, LitTarget = null!;
        public ID3D11ShaderResourceView ColourRead = null!, LitRead = null!;
        public int W, H;
        public void Dispose()
        {
            ColourTarget.Dispose(); LitTarget.Dispose(); ColourRead.Dispose(); LitRead.Dispose();
            Colour.Dispose(); Lit.Dispose();
        }
    }

    private Canvas? _view, _screen;
    private GpuTexture? _viewLit, _screenLit;

    /// <summary>A picture on the card, with its smaller copies.</summary>
    private sealed class Held : IDisposable
    {
        public ID3D11Texture2D Texture = null!;
        public ID3D11ShaderResourceView Read = null!;
        public long UsedAt;
        public void Dispose() { Read.Dispose(); Texture.Dispose(); }
    }

    private readonly Dictionary<object, Held> _pictures = new(ReferenceEqualityComparer.Instance);
    private Held? _disc, _solid;
    private long _frame;

    // The lights' tables: the lookups once, the rest for the lights last drawn with.
    private GpuTexture? _look, _angle, _falloff;
    private float[]? _falloffOf;
    private ID3D11Buffer? _decode, _texels, _rows, _glows;
    private ID3D11ShaderResourceView? _decodeRead, _texelsRead, _rowsRead, _glowsRead;
    private float[]? _decodeOf;
    private PlaceLights? _lightsOf;
    private int _lightRows;

    private GpuPlaceScene(GpuDevice gpu)
    {
        _gpu = gpu;
        string scene = GpuPrograms.Source("PlaceScene");
        _vertex = gpu.Vertex(scene, "Vertex", "PlaceScene");
        _pixel = gpu.Pixel(scene, "Pixel", "PlaceScene");
        _lit = gpu.Kernel(GpuPrograms.Source("PlaceLit"), "Lit", "PlaceLit");
        // Premultiplied over what is there, as WPF blends.
        _over = gpu.Device.CreateBlendState(new BlendDescription(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha));
        _raster = gpu.Device.CreateRasterizerState(RasterizerDescription.CullNone);
        _nearest = gpu.Device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipPoint, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp,
                                                                        0f, 1, ComparisonFunction.Never, 0f, 0f));
        _linear = gpu.Device.CreateSamplerState(new SamplerDescription(Filter.MinMagLinearMipPoint, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp,
                                                                       0f, 1, ComparisonFunction.Never, 0f, 0f));
        _smooth = gpu.Device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp,
                                                                       0f, 1, ComparisonFunction.Never, 0f, float.MaxValue));
        _itemCb = new GpuConstants(gpu, 24);
        _litCb = new GpuConstants(gpu, 20);
    }

    /// <summary>One for a preview, or null when the PC has no card to draw on.</summary>
    public static GpuPlaceScene? TryCreate()
    {
        var gpu = GpuDevice.Shared;
        if (gpu == null) return null;
        lock (gpu.Gate) return new GpuPlaceScene(gpu);
    }

    /// <summary>
    /// The frame through the game's look, into <paramref name="output"/> (BGRA,
    /// the view's pixels, rows from the top) - as GamePost.LookOverView gives it
    /// from the preview's own captures.
    /// </summary>
    public void Draw(PlaceFrame f, GpuPost post, byte[] output)
    {
        if (f.Scene == null) throw new InvalidOperationException("a frame with no look to draw");   // English on purpose: a programming error, never shown
        lock (_gpu.Gate)
        {
            // The shaders' pictures first: each sets the card up for itself.
            foreach (var first in f.DrawFirst) first();
            var ctx = _gpu.Clean();
            _frame++;
            bool bloom = f.Parts.Bloom;
            Raster(ctx, f, ref _view, f.Vw, f.Vh, f.ViewX, f.ViewY, f.ViewW, f.ViewH);
            if (bloom) Raster(ctx, f, ref _screen, ScreenW, ScreenH, 0, 0, f.CanvasW, f.CanvasH);
            ctx.ClearState();

            // A view pixel's centre on the screen: uv, v up (past its edges too) - PlacePreview.LookTick's.
            float ax = (float)(f.ViewW / f.Vw / f.CanvasW), bx = (float)(f.ViewX / f.CanvasW);
            float ay = (float)(-f.ViewH / f.Vh / f.CanvasH), by = (float)(1.0 - f.ViewY / f.CanvasH);
            Light(ctx, f, _view!, ref _viewLit, false, ax, bx, ay, by);
            if (bloom) Light(ctx, f, _screen!, ref _screenLit, true, 0f, 0f, 0f, 0f);
            post.LookFromCard(_viewLit!, bloom ? _screenLit : null, f.Vw, f.Vh, ax, bx, ay, by, f.Scene, f.Parts, output);
            LetGo();
        }
    }

    private ID3D11Texture2D? _captureBack;
    private int _captureW, _captureH;

    /// <summary>
    /// The frame's scene alone, as drawn - what a capture of the world layer gives (premultiplied BGRA, the
    /// frame's pixels, rows from the top) - and, when asked, its lit mask: for the character standing in the
    /// place, whose backdrop is made from it (PlacePreview.BuildStandInScene).
    /// </summary>
    public void Capture(PlaceFrame f, byte[] colour, byte[]? lit)
    {
        lock (_gpu.Gate)
        {
            foreach (var first in f.DrawFirst) first();
            var ctx = _gpu.Clean();
            _frame++;
            Canvas? canvas = f.Vw == ScreenW && f.Vh == ScreenH ? _screen : _view;
            Raster(ctx, f, ref canvas, f.Vw, f.Vh, f.ViewX, f.ViewY, f.ViewW, f.ViewH);
            if (f.Vw == ScreenW && f.Vh == ScreenH) _screen = canvas; else _view = canvas;
            ctx.ClearState();
            if (_captureBack == null || _captureW != f.Vw || _captureH != f.Vh)
            {
                _captureBack?.Dispose();
                _captureBack = _gpu.Device.CreateTexture2D(new Texture2DDescription(Bytes, (uint)f.Vw, (uint)f.Vh, 1, 1, BindFlags.None,
                                                                                    ResourceUsage.Staging, CpuAccessFlags.Read));
                _captureW = f.Vw; _captureH = f.Vh;
            }
            Back(ctx, canvas!.Colour, colour, f.Vw, f.Vh);
            if (lit != null) Back(ctx, canvas.Lit, lit, f.Vw, f.Vh);
            LetGo();
        }
    }

    private void Back(ID3D11DeviceContext ctx, ID3D11Texture2D from, byte[] into, int w, int h)
    {
        ctx.CopyResource(_captureBack!, from);
        var map = ctx.Map(_captureBack!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(map.DataPointer + y * (int)map.RowPitch, into, y * w * 4, w * 4);
        }
        finally { ctx.Unmap(_captureBack!, 0); }
    }

    // ── The scene, picture by picture ───────────────────────────────────

    private void Raster(ID3D11DeviceContext ctx, PlaceFrame f, ref Canvas? canvas, int w, int h, double viewX, double viewY, double viewW, double viewH)
    {
        if (canvas == null || canvas.W != w || canvas.H != h)
        {
            canvas?.Dispose();
            canvas = NewCanvas(w, h);
        }
        ctx.OMSetRenderTargets(new[] { canvas.ColourTarget, canvas.LitTarget });
        ctx.RSSetViewport(0f, 0f, w, h);
        var nothing = new Color4(0f, 0f, 0f, 0f);
        ctx.ClearRenderTargetView(canvas.ColourTarget, nothing);
        ctx.ClearRenderTargetView(canvas.LitTarget, nothing);
        ctx.OMSetBlendState(_over);
        ctx.RSSetState(_raster);
        ctx.IASetInputLayout(null);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        ctx.VSSetShader(_vertex);
        ctx.PSSetShader(_pixel);
        ctx.VSSetConstantBuffer(0, _itemCb.Buffer);
        ctx.PSSetConstantBuffer(0, _itemCb.Buffer);
        ctx.PSSetSampler(0, _nearest);
        ctx.PSSetSampler(1, _linear);
        ctx.PSSetSampler(2, _smooth);

        static float U(int v) => BitConverter.Int32BitsToSingle(v);
        Span<float> cb = stackalloc float[24];
        // A target pixel in canvas units, each way: what a picture's sides are drawn out by.
        double pixelX = viewW / w, pixelY = viewH / h;
        // Canvas -> clip: x = (cx - viewX) * 2 / viewW - 1, y = 1 - (cy - viewY) * 2 / viewH.
        double kx = 2.0 / viewW, ky = -2.0 / viewH;
        for (int i = 0; i < f.Count; i++)
        {
            ref var item = ref f.Items[i];
            if (!(item.Opacity > 0f)) continue;
            ID3D11ShaderResourceView? picture = null, bytes = null;
            int bw = 0, bh = 0;
            if (item.OnCard != null)
            {
                (bytes, bw, bh) = item.OnCard.Drawn();
                if (bytes == null || bw <= 0 || bh <= 0) continue;
            }
            else
            {
                picture = PictureOf(ctx, item);
                if (picture == null) continue;
            }
            cb[0] = (float)(item.M11 * kx); cb[1] = (float)(item.M21 * kx); cb[2] = (float)((item.OffsetX - viewX) * kx - 1.0); cb[3] = 0f;
            cb[4] = (float)(item.M12 * ky); cb[5] = (float)(item.M22 * ky); cb[6] = (float)((item.OffsetY - viewY) * ky + 1.0); cb[7] = 0f;
            cb[8] = item.R; cb[9] = item.G; cb[10] = item.B; cb[11] = item.A;
            cb[12] = item.Opacity; cb[13] = U((int)item.Kind); cb[14] = U((int)item.Sampling); cb[15] = U(bytes != null ? 1 : 0);
            cb[16] = U(bw); cb[17] = U(bh); cb[18] = U(item.Lit); cb[19] = 0f;
            // A pixel in the picture's own u and v: over the length, in pixels, of each of its sides.
            double sideU = Math.Sqrt(item.M11 / pixelX * (item.M11 / pixelX) + item.M12 / pixelY * (item.M12 / pixelY));
            double sideV = Math.Sqrt(item.M21 / pixelX * (item.M21 / pixelX) + item.M22 / pixelY * (item.M22 / pixelY));
            cb[20] = (float)Math.Min(4.0, 1.0 / Math.Max(sideU, 1e-6)); cb[21] = (float)Math.Min(4.0, 1.0 / Math.Max(sideV, 1e-6)); cb[22] = 0f; cb[23] = 0f;
            _itemCb.Upload(_gpu, cb);
            ctx.PSSetShaderResource(0, picture!);
            ctx.PSSetShaderResource(1, bytes!);
            ctx.Draw(4, 0);
        }
    }

    private Canvas NewCanvas(int w, int h)
    {
        var desc = new Texture2DDescription(Bytes, (uint)w, (uint)h, 1, 1, BindFlags.ShaderResource | BindFlags.RenderTarget);
        var c = new Canvas { W = w, H = h, Colour = _gpu.NewTexture(desc), Lit = _gpu.NewTexture(desc) };
        c.ColourTarget = _gpu.Device.CreateRenderTargetView(c.Colour);
        c.LitTarget = _gpu.Device.CreateRenderTargetView(c.Lit);
        c.ColourRead = _gpu.Device.CreateShaderResourceView(c.Colour);
        c.LitRead = _gpu.Device.CreateShaderResourceView(c.Lit);
        return c;
    }

    private ID3D11ShaderResourceView? PictureOf(ID3D11DeviceContext ctx, in PlaceItem item)
    {
        if (item.BuiltIn == PlaceBuiltIn.Disc) return (_disc ??= Keep(ctx, DiscPixels(), DiscSize, DiscSize)).Read;
        if (item.BuiltIn == PlaceBuiltIn.Solid) return (_solid ??= Keep(ctx, new byte[] { 255, 255, 255, 255 }, 1, 1)).Read;
        object? key = (object?)item.Bitmap ?? item.Pixels;
        if (key == null) return null;
        if (!_pictures.TryGetValue(key, out var held))
        {
            byte[] px; int w, h;
            if (item.Bitmap != null) px = Pbgra(item.Bitmap, out w, out h);
            else { px = item.Pixels!; w = item.PixelsW; h = item.PixelsH; }
            if (w <= 0 || h <= 0 || px.Length < w * h * 4) return null;
            _pictures[key] = held = Keep(ctx, px, w, h);
        }
        held.UsedAt = _frame;
        return held.Read;
    }

    /// <summary>A picture's premultiplied BGRA (a frozen picture: read on this thread).</summary>
    internal static byte[] Pbgra(BitmapSource bmp, out int w, out int h)
    {
        BitmapSource src = bmp.Format == PixelFormats.Pbgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Pbgra32, null, 0);
        w = src.PixelWidth; h = src.PixelHeight;
        var px = new byte[w * h * 4];
        src.CopyPixels(px, w * 4, 0);
        return px;
    }

    /// <summary>The picture on the card with its smaller copies made (each half the one before, as a HighQuality
    /// picture is read when drawn small).</summary>
    private Held Keep(ID3D11DeviceContext ctx, byte[] pbgra, int w, int h)
    {
        var desc = new Texture2DDescription(Bytes, (uint)w, (uint)h, 1, 0, BindFlags.ShaderResource | BindFlags.RenderTarget,
                                            ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.GenerateMips);
        var texture = _gpu.Device.CreateTexture2D(desc);
        unsafe
        {
            fixed (byte* p = pbgra)
                ctx.UpdateSubresource(texture, 0, null, (IntPtr)p, (uint)(w * 4), 0);
        }
        var read = _gpu.Device.CreateShaderResourceView(texture);
        ctx.GenerateMips(read);
        return new Held { Texture = texture, Read = read, UsedAt = _frame };
    }

    private const int DiscSize = 256;

    /// <summary>A white disc touching its square's sides, its edge a pixel soft: what an Ellipse is filled through.</summary>
    private static byte[] DiscPixels()
    {
        var px = new byte[DiscSize * DiscSize * 4];
        const float r = DiscSize / 2f;
        for (int y = 0; y < DiscSize; y++)
            for (int x = 0; x < DiscSize; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float cover = Math.Clamp(r - MathF.Sqrt(dx * dx + dy * dy) + 0.5f, 0f, 1f);
                byte a = (byte)(cover * 255f + 0.5f);
                int i = (y * DiscSize + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = px[i + 3] = a;
            }
        return px;
    }

    /// <summary>Pictures no frame has drawn for a while (another place's) leave the card.</summary>
    private void LetGo()
    {
        if ((_frame & 255) != 0 || _pictures.Count == 0) return;
        List<object>? gone = null;
        foreach (var (key, held) in _pictures)
            if (_frame - held.UsedAt > 600) (gone ??= new()).Add(key);
        if (gone == null) return;
        foreach (var key in gone)
        {
            _pictures[key].Dispose();
            _pictures.Remove(key);
        }
    }

    // ── Into the camera's target, lit ───────────────────────────────────

    private void Light(ID3D11DeviceContext ctx, PlaceFrame f, Canvas canvas, ref GpuTexture? target, bool screenRows, float ax, float bx, float ay, float by)
    {
        if (target == null || target.W != canvas.W || target.H != canvas.H)
        {
            target?.Dispose();
            target = GpuTexture.Create<float>(_gpu, canvas.W, canvas.H, Format.R32G32B32A32_Float, true);
        }
        var decode = f.Scene!.Decode;
        if (!ReferenceEquals(_decodeOf, decode))
        {
            _decodeRead?.Dispose(); _decode?.Dispose();
            (_decode, _decodeRead) = Floats(decode, 4);
            _decodeOf = decode;
        }
        var lights = f.Lights;
        if (lights != null) Tables(lights);

        static float U(int v) => BitConverter.Int32BitsToSingle(v);
        var (tw, th) = lights?.TextureSize ?? (0, 0);
        // The NPCs' wet drops, and the particle texture's copies (once).
        var mips = f.DropCount > 0 ? WetDrops.Texture : null;
        int drops = mips != null ? DropsOnCard(ctx, f) : 0;
        float canvasW = (float)f.CanvasW, canvasH = (float)f.CanvasH;
        // WetDrops.Over: the target's pixels to a canvas pixel, as the processor works it out.
        float dropScale = screenRows ? canvas.W / canvasW : 1f / (ax * canvasW);
        _litCb.Upload(_gpu, new[]
        {
            U(canvas.W), U(canvas.H), U(screenRows ? 1 : 0), U(f.Masked && lights != null ? 1 : 0),
            ax, bx, ay, by,
            U(lights != null ? 1 : 0), U(lights != null ? _lightRows : 0), U(tw), U(th),
            U(drops), U(mips?.Full ?? 0), U(mips?.Levels ?? 0), dropScale,
            canvasW, canvasH, 0f, 0f,
        });
        ctx.CSSetShader(_lit);
        ctx.CSSetConstantBuffer(0, _litCb.Buffer);
        ctx.CSSetShaderResource(0, canvas.ColourRead);
        ctx.CSSetShaderResource(1, canvas.LitRead);
        ctx.CSSetShaderResource(2, _decodeRead!);
        if (lights != null)
        {
            ctx.CSSetShaderResource(3, _texelsRead!);
            ctx.CSSetShaderResource(4, _look!.Read);
            ctx.CSSetShaderResource(5, _angle!.Read);
            ctx.CSSetShaderResource(6, _falloff!.Read);
            if (_rowsRead != null) ctx.CSSetShaderResource(7, _rowsRead);
            if (_glowsRead != null) ctx.CSSetShaderResource(8, _glowsRead);
        }
        if (drops > 0)
        {
            ctx.CSSetShaderResource(9, _dropsRead!);
            ctx.CSSetShaderResource(10, _dropAlphaRead!);
        }
        ctx.CSSetUnorderedAccessView(0, target.Write!);
        ctx.Dispatch((uint)((canvas.W + 15) / 16), (uint)((canvas.H + 15) / 16), 1);
        ctx.CSSetUnorderedAccessView(0, null);
        for (uint slot = 0; slot <= 10; slot++) ctx.CSSetShaderResource(slot, null);
    }

    // The drops' rows (made again as their number grows; filled every frame) and the texture's copies.
    private ID3D11Buffer? _drops, _dropAlpha;
    private ID3D11ShaderResourceView? _dropsRead, _dropAlphaRead;
    private int _dropsRoom;
    private float[]? _dropRows;
    private const int DropStride = 12;

    /// <summary>The frame's drops on the card, for both of its targets: how many.</summary>
    private int DropsOnCard(ID3D11DeviceContext ctx, PlaceFrame f)
    {
        var mips = WetDrops.Texture!;
        if (_dropAlpha == null)
            (_dropAlpha, _dropAlphaRead) = Floats(mips.Alpha, 4);
        if (_drops == null || _dropsRoom < f.DropCount)
        {
            _dropsRead?.Dispose(); _drops?.Dispose();
            _dropsRoom = Math.Max(16, f.DropCount * 2);
            _dropRows = new float[_dropsRoom * DropStride];
            (_drops, _dropsRead) = Floats(_dropRows, DropStride * 4);
        }
        var rows = _dropRows!;
        Array.Clear(rows);
        for (int i = 0; i < f.DropCount; i++)
        {
            ref readonly var d = ref f.Drops[i];
            int o = i * DropStride;
            rows[o] = (float)d.X; rows[o + 1] = (float)d.Y; rows[o + 2] = (float)d.W; rows[o + 3] = (float)d.H; rows[o + 4] = d.Alpha;
            rows[o + 5] = WetDrops.Linear(d.R); rows[o + 6] = WetDrops.Linear(d.G); rows[o + 7] = WetDrops.Linear(d.B); rows[o + 8] = d.Solid ? 1f : 0f; rows[o + 9] = d.Lit ? 1f : 0f;
        }
        unsafe
        {
            fixed (float* p = rows)
                ctx.UpdateSubresource(_drops!, 0, null, (IntPtr)p, 0, 0);
        }
        return f.DropCount;
    }

    private (ID3D11Buffer, ID3D11ShaderResourceView) Floats(ReadOnlySpan<float> data, int stride)
    {
        ID3D11Buffer buffer;
        unsafe
        {
            fixed (float* p = data)
                buffer = _gpu.Device.CreateBuffer(new BufferDescription((uint)(data.Length * 4), BindFlags.ShaderResource, ResourceUsage.Default,
                                                                        CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, (uint)stride), new SubresourceData((IntPtr)p));
        }
        return (buffer, _gpu.Device.CreateShaderResourceView(buffer));
    }

    /// <summary>The lights' tables on the card: made again only for other lights.</summary>
    private void Tables(PlaceLights lights)
    {
        _look ??= GpuTexture.Create<float>(_gpu, 256, 256, Format.R32_Float, false, PlaceLights.LookTable);
        _angle ??= GpuTexture.Create<float>(_gpu, 256, 256, Format.R32_Float, false, PlaceLights.AngleTable);
        var falloff = lights.FalloffTexels;
        if (!ReferenceEquals(_falloffOf, falloff))
        {
            _falloff?.Dispose();
            _falloff = GpuTexture.Create<float>(_gpu, FalloffLookup.Width, FalloffLookup.Height, Format.R32_Float, false, falloff);
            _falloffOf = falloff;
        }
        if (ReferenceEquals(_lightsOf, lights)) return;
        _texelsRead?.Dispose(); _texels?.Dispose(); _rowsRead?.Dispose(); _rows?.Dispose(); _glowsRead?.Dispose(); _glows?.Dispose();
        _rows = null; _rowsRead = null; _glows = null; _glowsRead = null;
        (_texels, _texelsRead) = Floats(lights.Texels, 4);

        // The lights with a glow, in the place's order (PlaceLights.Apply's loop).
        static float U(int v) => BitConverter.Int32BitsToSingle(v);
        var rows = new List<float>();
        var glows = new List<float[]>();
        var all = lights.Lights;
        var shaped = lights.ShapedGlows;
        for (int k = 0; k < all.Count; k++)
        {
            var l = all[k];
            if (l.VolumeOpacity <= 0f) continue;
            bool isShaped = shaped[k] != null;
            if (!isShaped && l.Shape != null) continue;
            float turn = l.Rotation * MathF.PI / 180f;
            rows.AddRange(new[]
            {
                l.X, l.Y, l.Radius, l.VolumeOpacity,
                MathF.Cos(turn), MathF.Sin(turn), l.InnerRadiusMult, l.InnerAngle,
                l.OuterAngle, l.R, l.G, l.B,
                l.Falloff, U(isShaped ? 1 : 0), U(glows.Count * ScreenW * ScreenH * 3), U(l.Rotation != 0f ? 1 : 0),
            });
            if (isShaped) glows.Add(shaped[k]!);
        }
        _lightRows = rows.Count / 16;
        if (_lightRows > 0) (_rows, _rowsRead) = Floats(rows.ToArray(), 64);
        if (glows.Count > 0)
        {
            var joined = new float[glows.Count * ScreenW * ScreenH * 3];
            for (int g = 0; g < glows.Count; g++) Array.Copy(glows[g], 0, joined, g * ScreenW * ScreenH * 3, ScreenW * ScreenH * 3);
            (_glows, _glowsRead) = Floats(joined, 4);
        }
        _lightsOf = lights;
    }

    public void Dispose()
    {
        lock (_gpu.Gate)
        {
            foreach (var held in _pictures.Values) held.Dispose();
            _pictures.Clear();
            _disc?.Dispose(); _solid?.Dispose(); _captureBack?.Dispose();
            _view?.Dispose(); _screen?.Dispose(); _viewLit?.Dispose(); _screenLit?.Dispose();
            _look?.Dispose(); _angle?.Dispose(); _falloff?.Dispose();
            _decodeRead?.Dispose(); _decode?.Dispose(); _texelsRead?.Dispose(); _texels?.Dispose();
            _rowsRead?.Dispose(); _rows?.Dispose(); _glowsRead?.Dispose(); _glows?.Dispose();
            _dropsRead?.Dispose(); _drops?.Dispose(); _dropAlphaRead?.Dispose(); _dropAlpha?.Dispose();
            _over.Dispose(); _raster.Dispose(); _nearest.Dispose(); _linear.Dispose(); _smooth.Dispose();
            _itemCb.Dispose(); _litCb.Dispose();
        }
    }
}
