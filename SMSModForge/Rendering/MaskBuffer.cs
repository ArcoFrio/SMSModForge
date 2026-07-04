using System;

namespace SMSModForge.Rendering;

/// <summary>
/// In-memory mask under edit. The on-disk PNG is decoded into <em>premultiplied</em>
/// form: each loaded R/G/B is multiplied by its alpha and A is reset to 255.
/// This collapses the file's <c>channel × alpha</c> shader contribution into a
/// single number per channel, so the editor can treat R, G, B as fully
/// independent intensity planes — painting one channel can never amplify
/// another. The visual effect under the shader is invariant under this
/// transform: <c>(c · α) · 1 = c · α</c>.
/// <para/>
/// Channel-to-effect mapping (matches <see cref="JiggleShader"/>):
/// <list type="bullet">
///   <item>R → bounce</item>
///   <item>G → sway</item>
///   <item>B → wave (gradient noise)</item>
/// </list>
/// </summary>
public sealed class MaskBuffer
{
    public const int Size = JiggleShader.Size;
    public const int BgraStride = Size * 4;

    public byte[] R { get; } = new byte[Size * Size];
    public byte[] G { get; } = new byte[Size * Size];
    public byte[] B { get; } = new byte[Size * Size];
    public byte[] A { get; } = new byte[Size * Size];

    public MaskBuffer()
    {
        // Premultiplied invariant: A is always 255. Anything that mutates the
        // mask should preserve this.
        Array.Fill(A, (byte)255);
    }

    /// <summary>0 = R, 1 = G, 2 = B.</summary>
    public byte[] Channel(int idx) => idx switch
    {
        0 => R,
        1 => G,
        2 => B,
        _ => throw new ArgumentOutOfRangeException(nameof(idx)),
    };

    /// <summary>
    /// Composite the four channels into a BGRA buffer. A is always 255 by
    /// invariant, so the shader sees the premultiplied channel values as the
    /// effective <c>channel × alpha</c> term directly.
    /// </summary>
    public void ToBgra(byte[] dest)
    {
        if (dest.Length != Size * Size * 4) throw new ArgumentException("size mismatch", nameof(dest));
        for (int i = 0, j = 0; i < Size * Size; i++, j += 4)
        {
            dest[j    ] = B[i];
            dest[j + 1] = G[i];
            dest[j + 2] = R[i];
            dest[j + 3] = 255;
        }
    }

    /// <summary>
    /// Decode a loaded BGRA buffer into the in-memory channel planes,
    /// premultiplying R/G/B by alpha and normalising A to 255. Effects are
    /// preserved exactly (the shader does <c>c · α</c> regardless), and the
    /// editor no longer has to grapple with a shared, mutable alpha.
    /// </summary>
    public void FromBgra(byte[] src)
    {
        if (src.Length != Size * Size * 4) throw new ArgumentException("size mismatch", nameof(src));
        for (int i = 0, j = 0; i < Size * Size; i++, j += 4)
        {
            byte a = src[j + 3];
            B[i] = (byte)((src[j    ] * a + 127) / 255);
            G[i] = (byte)((src[j + 1] * a + 127) / 255);
            R[i] = (byte)((src[j + 2] * a + 127) / 255);
            A[i] = 255;
        }
    }

    public void ClearChannel(int idx) => Array.Clear(Channel(idx));
}
