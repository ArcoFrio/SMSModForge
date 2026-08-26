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
///   <item>G → wave</item>
///   <item>B → noise (gradient noise)</item>
/// </list>
/// </summary>
/// <summary>
/// Which shader a mask is being authored for. The two read a mask completely
/// differently, so a buffer has to know which it is.
/// </summary>
public enum MaskKind
{
    /// <summary><c>Sprites/JiggleSprite</c> — busts and NPCs. Three independent
    /// intensity planes in R/G/B, alpha normalised away.</summary>
    BustRgb,

    /// <summary><c>URP/2D/Sprite-Lit-Jiggle</c> — levels. One intensity plane,
    /// and it lives in ALPHA: the Milking fragment samples the mask and uses
    /// nothing but <c>.a</c>. Painting R/G/B for a level mask does nothing at
    /// all, which is why this kind exists rather than reusing channel 0.</summary>
    LevelAlpha,
}

public sealed class MaskBuffer
{
    public const int Size = JiggleShader.Size;
    public const int BgraStride = Size * 4;

    public MaskKind Kind { get; }


    /// <summary>Editable planes: three for a bust mask, one for a level's.</summary>
    public int ChannelCount => Kind == MaskKind.LevelAlpha ? 1 : 3;

    /// <summary>Names matching what each plane actually drives.</summary>
    public string ChannelName(int idx) => Kind == MaskKind.LevelAlpha
        ? "Intensity"
        : idx switch { 0 => "Bounce", 1 => "Wave", _ => "Noise" };

    public byte[] R { get; } = new byte[Size * Size];
    public byte[] G { get; } = new byte[Size * Size];
    public byte[] B { get; } = new byte[Size * Size];
    public byte[] A { get; } = new byte[Size * Size];

    public MaskBuffer() : this(MaskKind.BustRgb) { }

    public MaskBuffer(MaskKind kind)
    {
        Kind = kind;
        // Premultiplied invariant: A is always 255. Anything that mutates the
        // mask should preserve this. A LEVEL mask breaks that deliberately —
        // alpha IS its payload there — so it starts empty instead.
        if (kind != MaskKind.LevelAlpha) Array.Fill(A, (byte)255);
    }

    /// <summary>Bust: 0 = R, 1 = G, 2 = B. Level: 0 = the alpha plane.</summary>
    public byte[] Channel(int idx) => Kind == MaskKind.LevelAlpha
        ? (idx == 0 ? A : throw new ArgumentOutOfRangeException(nameof(idx)))
        : idx switch
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
        if (Kind == MaskKind.LevelAlpha)
        {
            // White with the intensity in alpha. White because the level shader
            // reads only .a, so the colour is free — and a white PNG makes the
            // painted region legible in any external image editor.
            for (int i = 0, j = 0; i < Size * Size; i++, j += 4)
            {
                dest[j] = dest[j + 1] = dest[j + 2] = 255;
                dest[j + 3] = A[i];
            }
            return;
        }
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
        if (Kind == MaskKind.LevelAlpha)
        {
            // Alpha straight in, no premultiply: it is the payload, not a
            // modifier on something else.
            for (int i = 0, j = 0; i < Size * Size; i++, j += 4) A[i] = src[j + 3];
            return;
        }
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
