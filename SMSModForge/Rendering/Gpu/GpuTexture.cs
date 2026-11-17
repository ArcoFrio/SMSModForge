using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SMSModForge.Rendering.Gpu;

/// <summary>
/// A picture on the graphics card: what a kernel reads (and, when writable,
/// writes), in one of the formats the game's own frames are kept in.
/// </summary>
internal sealed class GpuTexture : IDisposable
{
    public ID3D11Texture2D Texture { get; }
    public ID3D11ShaderResourceView Read { get; }
    public ID3D11UnorderedAccessView? Write { get; }
    public int W { get; }
    public int H { get; }
    public Format Format { get; }

    private GpuTexture(ID3D11Texture2D texture, ID3D11ShaderResourceView read, ID3D11UnorderedAccessView? write, int w, int h, Format format)
    {
        Texture = texture; Read = read; Write = write; W = w; H = h; Format = format;
    }

    /// <summary>A <paramref name="w"/> x <paramref name="h"/> picture, empty or
    /// holding <paramref name="initial"/> (rows from the top, tightly packed).</summary>
    public static GpuTexture Create<T>(GpuDevice gpu, int w, int h, Format format, bool writable, ReadOnlySpan<T> initial = default) where T : unmanaged
    {
        var desc = new Texture2DDescription(format, (uint)w, (uint)h, 1, 1,
                                            writable ? BindFlags.ShaderResource | BindFlags.UnorderedAccess : BindFlags.ShaderResource);
        ID3D11Texture2D texture;
        if (initial.IsEmpty) texture = gpu.NewTexture(desc);
        else
        {
            unsafe
            {
                fixed (T* p = initial)
                    texture = gpu.Device.CreateTexture2D(desc, new SubresourceData((IntPtr)p, (uint)(initial.Length * sizeof(T) / h)));
            }
        }
        var read = gpu.Device.CreateShaderResourceView(texture);
        var write = writable ? gpu.Device.CreateUnorderedAccessView(texture) : null;
        return new GpuTexture(texture, read, write, w, h, format);
    }

    /// <summary>Replace the whole picture (rows from the top, tightly packed).</summary>
    public void Upload<T>(GpuDevice gpu, ReadOnlySpan<T> data) where T : unmanaged
    {
        unsafe
        {
            fixed (T* p = data)
                gpu.Context.UpdateSubresource(Texture, 0, null, (IntPtr)p, (uint)(data.Length * sizeof(T) / H), 0);
        }
    }

    public void Dispose()
    {
        Write?.Dispose();
        Read.Dispose();
        Texture.Dispose();
    }
}

/// <summary>
/// Copies a picture back from the card, for the editor's bitmaps (and tests).
/// </summary>
internal sealed class GpuReadback : IDisposable
{
    private readonly ID3D11Texture2D _staging;
    public int W { get; }
    public int H { get; }

    public GpuReadback(GpuDevice gpu, int w, int h, Format format)
    {
        W = w; H = h;
        _staging = gpu.Device.CreateTexture2D(new Texture2DDescription(format, (uint)w, (uint)h, 1, 1, BindFlags.None,
                                                                       ResourceUsage.Staging, CpuAccessFlags.Read));
    }

    /// <summary>Copy <paramref name="source"/> into <paramref name="into"/>
    /// (rows from the top, tightly packed), waiting for the card to finish it.</summary>
    public void Read<T>(GpuDevice gpu, GpuTexture source, Span<T> into) where T : unmanaged
    {
        gpu.Context.CopyResource(_staging, source.Texture);
        var map = gpu.Context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowBytes = into.Length * Marshal.SizeOf<T>() / H;
            var bytes = MemoryMarshal.AsBytes(into);
            unsafe
            {
                for (int y = 0; y < H; y++)
                    new ReadOnlySpan<byte>((byte*)map.DataPointer + (long)y * map.RowPitch, rowBytes).CopyTo(bytes.Slice(y * rowBytes, rowBytes));
            }
        }
        finally { gpu.Context.Unmap(_staging, 0); }
    }

    public void Dispose() => _staging.Dispose();
}

/// <summary>A kernel's settings: a constant buffer of floats (and the ints
/// beside them), uploaded before each run.</summary>
internal sealed class GpuConstants : IDisposable
{
    public ID3D11Buffer Buffer { get; }
    public int Floats { get; }

    public GpuConstants(GpuDevice gpu, int floats)
    {
        Floats = (floats + 3) / 4 * 4;
        Buffer = gpu.Device.CreateBuffer(new BufferDescription((uint)(Floats * 4), BindFlags.ConstantBuffer));
    }

    public void Upload(GpuDevice gpu, ReadOnlySpan<float> values)
    {
        ReadOnlySpan<float> data = values;
        if (values.Length != Floats)
        {
            var padded = new float[Floats];
            values.CopyTo(padded);
            data = padded;
        }
        unsafe
        {
            fixed (float* p = data)
                gpu.Context.UpdateSubresource(Buffer, 0, null, (IntPtr)p, 0, 0);
        }
    }

    public void Dispose() => Buffer.Dispose();
}
