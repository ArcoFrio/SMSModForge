using System;
using System.Collections.Concurrent;
using System.IO;

namespace SMSModForge.Rendering.Gpu;

/// <summary>
/// The HLSL the previews' kernels are made of, shipped inside the editor
/// (Rendering/Gpu/Programs): the game's own programs as
/// Tools/Shaders/dxbc_to_hlsl.py generated them (GameBustProgram,
/// GamePostProgram) and the kernels around them.
/// </summary>
internal static class GpuPrograms
{
    private static readonly ConcurrentDictionary<string, string> _sources = new();

    /// <summary>The text of Programs/<paramref name="name"/>.hlsl.</summary>
    public static string Source(string name) => _sources.GetOrAdd(name, n =>
    {
        using var stream = typeof(GpuPrograms).Assembly.GetManifestResourceStream("Gpu." + n + ".hlsl")
                           ?? throw new InvalidOperationException("no preview program " + n);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
