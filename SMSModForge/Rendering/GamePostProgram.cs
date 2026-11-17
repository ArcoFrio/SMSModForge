using System;
using System.Runtime.CompilerServices;

namespace SMSModForge.Rendering;

/// <summary>The game's post-processing programs (Hidden/Kronnect/Beautify),
/// turned into C# instruction for instruction - GamePostProgram.g.cs; see
/// <see cref="GamePost"/> for how they run. The same helpers as
/// <see cref="GameBustProgram"/>'s: the GPU's bits for comparisons.</summary>
internal static partial class GamePostProgram
{
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
