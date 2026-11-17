using System;
using System.Runtime.CompilerServices;

namespace SMSModForge.Rendering;

/// <summary>
/// The game's bust pixel program (Sprites/JiggleSprite_FakeGI_2DLit, Universal2D
/// pass, the variant a bust in the player's room runs: one 2D light texture,
/// the fake GI on), turned into C# instruction for instruction by
/// Tools/Shaders/dxbc_to_cs.py - GameBustProgram.g.cs. <see cref="Program"/> is
/// all of it, the exact reference; the fake GI's three parts
/// (<see cref="Gather"/>, <see cref="Blur"/>, <see cref="Finish"/>) are what
/// <see cref="FakeGi"/> runs, so that the blur can come from a table.
/// <para/>
/// Constants, as the game packs them: cb0[4].x the mip bias, [15] _Time,
/// [23] _ScreenParams, [129].xy the light's multiply and add, [130]/[131] its
/// mask filters, [134] = GIEnabled, GIIntensity, GIBlurRadius, GIBlurAmount;
/// [135] = GIEdgeWidth, GISaturation, GIBrightness; [136] GITint; [137] =
/// RimPower, RimSoftness, _HDREmulationScale. cb1 = UnityPerMaterial: [0]
/// _MaskTex_ST, [1] _Color, [2] _RendererColor, [3] JiggleSpeed, Strength,
/// Frequency, NoiseScale, [4] NoiseSpeed, NoiseStrength. Textures: t0 the light,
/// t1 the art (_MainTex), t2 the mask, t3 the background the fake GI reads
/// (_CameraSortingLayerTexture). Inputs: v1 the vertex colour, v2.xy the uv,
/// v2.zw the light's uv, v3 the screen position (xy / w, from the bottom left).
/// </summary>
internal static partial class GameBustProgram
{
    internal const int Cb0Size = 138 * 4, Cb1Size = 5 * 4;

    private static readonly float TrueBits = BitConverter.Int32BitsToSingle(-1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Nz(float x) => BitConverter.SingleToInt32Bits(x) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Mask(bool b) => b ? TrueBits : 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float And(float a, float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a) & BitConverter.SingleToInt32Bits(b));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Sat(float x) => x > 0f ? (x < 1f ? x : 1f) : 0f;
}

/// <summary>The program's registers, between one part of it and the next.</summary>
internal struct GameRegisters
{
    public float r0x, r0y, r0z, r0w, r1x, r1y, r1z, r1w, r2x, r2y, r2z, r2w, r3x, r3y, r3z, r3w;
    public float r4x, r4y, r4z, r4w, r5x, r5y, r5z, r5w, r6x, r6y, r6z, r6w, r7x, r7y, r7z, r7w;
    public float r8x, r8y, r8z, r8w, r9x, r9y, r9z, r9w, r10x, r10y, r10z, r10w, r11x, r11y, r11z, r11w;
    public float r12x, r12y, r12z, r12w, r13x, r13y, r13z, r13w, r14x, r14y, r14z, r14w, r15x, r15y, r15z, r15w;
    public float o0x, o0y, o0z, o0w;
    /// <summary>The pixel was discarded: nothing is drawn.</summary>
    public bool Discarded;
}

/// <summary>What the vertex stage hands a pixel.</summary>
internal struct GameInputs
{
    public float v1x, v1y, v1z, v1w, v2x, v2y, v2z, v2w, v3x, v3y, v3z, v3w;
}

/// <summary>The program's textures, sampled as the GPU would (LOD 0; the
/// sampler's filter and addressing are the texture's own): t0 to t3.</summary>
internal interface IGameTextures
{
    (float, float, float, float) S0(float u, float v);
    (float, float, float, float) S1(float u, float v);
    (float, float, float, float) S2(float u, float v);
    (float, float, float, float) S3(float u, float v);
}
