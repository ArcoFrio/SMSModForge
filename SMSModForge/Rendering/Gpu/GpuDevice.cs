using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace SMSModForge.Rendering.Gpu;

/// <summary>
/// The graphics card, for the previews (1.9.1, the author: "Can we really not
/// have the GPU help with rendering here?" - "for all options, not only
/// Ultra"). One Direct3D 11 device for the editor, made the first time it is
/// asked for; null when the PC has no card that does Direct3D 11 compute (the
/// previews then draw on the processor, as before).
/// <para/>
/// Its programs are HLSL compute kernels, compiled by Windows' own shader
/// compiler (d3dcompiler_47, part of every Windows 10 and 11) the first time
/// they are used and kept on disk by what they say, so a later start loads
/// them at once.
/// <para/>
/// The immediate context is one thread's at a time: everything that uses it
/// holds <see cref="Gate"/>.
/// </summary>
internal sealed class GpuDevice : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    /// <summary>The card's name, as Windows gives it.</summary>
    public string Adapter { get; }
    /// <summary>Windows' software device (WARP): a control for tests, never the previews' choice.</summary>
    public bool Software { get; }
    public readonly object Gate = new();

    private readonly ConcurrentDictionary<string, ID3D11ComputeShader> _kernels = new();

    private static readonly Lazy<GpuDevice?> _shared = new(() => TryCreate(false, out _sharedFailure));
    private static string? _sharedFailure;

    /// <summary>The editor's device, or null when there is none (see <see cref="SharedFailure"/>).</summary>
    public static GpuDevice? Shared => _shared.Value;

    /// <summary>Why there is no <see cref="Shared"/> device, when there is none.</summary>
    public static string? SharedFailure { get { _ = _shared.Value; return _sharedFailure; } }

    private GpuDevice(ID3D11Device device, ID3D11DeviceContext context, string adapter, bool software)
    {
        Device = device; Context = context; Adapter = adapter; Software = software;
    }

    /// <summary>A device on the PC's graphics card (or on Windows' software
    /// one, for tests), or null with <paramref name="why"/>.</summary>
    public static GpuDevice? TryCreate(bool software, out string? why)
    {
        why = null;
        try
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            var result = D3D11.D3D11CreateDevice(null, software ? DriverType.Warp : DriverType.Hardware, DeviceCreationFlags.None,
                                                 levels, out ID3D11Device? device, out ID3D11DeviceContext? context);
            if (result.Failure || device == null || context == null)
            {
                why = "Direct3D 11 is not available (" + result + ")";   // English on purpose: a diagnostic for the record, never shown
                return null;
            }
            string adapter = "?";
            try
            {
                using var dxgi = device.QueryInterface<Vortice.DXGI.IDXGIDevice>();
                using var a = dxgi.GetAdapter();
                adapter = a.Description.Description;
            }
            catch (Exception) { /* a name only */ }
            return new GpuDevice(device, context, adapter, software);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or SharpGen.Runtime.SharpGenException or InvalidOperationException)
        {
            why = e.Message;
            return null;
        }
    }

    /// <summary>
    /// The context for a new draw, with nothing left on it by the one before
    /// (1.9.1). Settings and pictures stay bound after a draw, and a preview's
    /// left in place changed the next preview's picture: once the Places preview
    /// had drawn its look zoomed in, the next bloom on the card - the bust's, an
    /// NPC's, the place's own - read its settings and came out wrong. Every draw
    /// starts here, holding <see cref="Gate"/>, and binds all it reads.
    /// </summary>
    public ID3D11DeviceContext Clean()
    {
        Context.ClearState();
        return Context;
    }

    /// <summary>
    /// What a new buffer or picture holds before the card writes it, in a test
    /// (1.9.1). Direct3D leaves one made without contents undefined: zeros as a
    /// rule, but beside other previews at work, the memory one of them has just
    /// let go of, its pictures still in it - so a draw that read a part it had
    /// not written would come out right on a quiet machine and wrong on a busy
    /// one. Every draw writes all it reads; a test proves it by filling each new
    /// one with this, on its own thread. Null, as outside tests: left to Direct3D.
    /// </summary>
    [ThreadStatic] internal static uint? NewContentForTests;

    /// <summary>A buffer the card writes: made empty, as every one is (see
    /// <see cref="NewContentForTests"/>).</summary>
    public ID3D11Buffer NewBuffer(BufferDescription desc)
    {
        if (NewContentForTests is not uint fill) return Device.CreateBuffer(desc);
        var words = new uint[(desc.ByteWidth + 3) / 4];
        Array.Fill(words, fill);
        unsafe { fixed (uint* p = words) return Device.CreateBuffer(desc, new SubresourceData((IntPtr)p)); }
    }

    /// <summary>A picture the card writes: made empty, as every one is (see
    /// <see cref="NewContentForTests"/>).</summary>
    public ID3D11Texture2D NewTexture(Texture2DDescription desc)
    {
        if (NewContentForTests is not uint fill) return Device.CreateTexture2D(desc);
        int bytesPerTexel = desc.Format switch
        {
            Vortice.DXGI.Format.R32G32B32A32_Float => 16,
            Vortice.DXGI.Format.R16G16B16A16_Float => 8,
            Vortice.DXGI.Format.R8G8B8A8_UInt or Vortice.DXGI.Format.R8G8B8A8_UNorm or Vortice.DXGI.Format.B8G8R8A8_UNorm
                or Vortice.DXGI.Format.R32_Float or Vortice.DXGI.Format.R32_UInt => 4,
            _ => throw new NotSupportedException("a test's fill for " + desc.Format),   // English on purpose: a test's own failure
        };
        int pitch = (int)desc.Width * bytesPerTexel;
        var words = new uint[(pitch * (int)desc.Height + 3) / 4];
        Array.Fill(words, fill);
        unsafe { fixed (uint* p = words) return Device.CreateTexture2D(desc, new SubresourceData((IntPtr)p, (uint)pitch)); }
    }

    /// <summary>The compute kernel <paramref name="entry"/> of
    /// <paramref name="source"/>, compiled once (and kept on disk).</summary>
    public ID3D11ComputeShader Kernel(string source, string entry, string name)
        => _kernels.GetOrAdd(entry + "\n" + source, _ => Device.CreateComputeShader(Bytecode(source, entry, name)));

    private readonly ConcurrentDictionary<string, ID3D11VertexShader> _vertex = new();
    private readonly ConcurrentDictionary<string, ID3D11PixelShader> _pixel = new();

    /// <summary>A vertex program (the place's scene, 1.10.0: the card draws its pictures as the game's own
    /// renderer does, as triangles), compiled once and kept on disk as the kernels are.</summary>
    public ID3D11VertexShader Vertex(string source, string entry, string name)
        => _vertex.GetOrAdd(entry + "\n" + source, _ => Device.CreateVertexShader(Bytecode(source, entry, name, "vs_5_0")));

    /// <summary>A pixel program, as <see cref="Vertex"/>.</summary>
    public ID3D11PixelShader Pixel(string source, string entry, string name)
        => _pixel.GetOrAdd(entry + "\n" + source, _ => Device.CreatePixelShader(Bytecode(source, entry, name, "ps_5_0")));

    /// <summary>The compiled program: from the disk cache when this source was
    /// compiled before, else compiled now and kept.</summary>
    internal static byte[] Bytecode(string source, string entry, string name, string Profile = "cs_5_0")
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Profile + "\n" + entry + "\n" + source)))[..32];
        string cached = Services.AppDataFolder.PathOf("ShaderCache", name + "-" + entry + "-" + key + ".cso");
        try { if (File.Exists(cached)) return File.ReadAllBytes(cached); }
        catch (IOException) { /* compile it */ }

        // IEEE strictness: no fast-math reassociation, so each operation is
        // the one the source says (the kernels mark fused operations themselves).
        var flags = ShaderFlags.OptimizationLevel3 | ShaderFlags.IeeeStrictness;
        var result = Compiler.Compile(source, null!, null!, entry, name, Profile, flags, EffectFlags.None, out Blob? code, out Blob? errors);
        string? messages = errors?.AsString();
        errors?.Dispose();
        if (result.Failure || code == null)
            throw new InvalidOperationException($"the preview program {name}/{entry} did not compile: {messages}");
        byte[] bytes = code.AsBytes();
        code.Dispose();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            File.WriteAllBytes(cached, bytes);
        }
        catch (IOException) { /* compiled again next time */ }
        catch (UnauthorizedAccessException) { }
        return bytes;
    }

    public void Dispose()
    {
        foreach (var k in _kernels.Values) k.Dispose();
        _kernels.Clear();
        foreach (var v in _vertex.Values) v.Dispose();
        _vertex.Clear();
        foreach (var p in _pixel.Values) p.Dispose();
        _pixel.Clear();
        Context.Dispose();
        Device.Dispose();
    }
}
