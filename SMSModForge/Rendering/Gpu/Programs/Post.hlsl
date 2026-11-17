// The game's post-processing on the graphics card (1.9.1): Beautify's passes (GamePostProgram,
// generated from the game's own compiled programs) blitting between half-float targets as
// GamePost.BloomAndFlares does on the processor, and the final pass - Beautify's compose, the
// camera's 11-11-10-bit target, URP's grading table, the screen's sRGB - as
// BackdropRender.Present does. GpuPostTests holds them to the processor's.
//
// Targets are rows from the bottom (v up), as GamePost.Target. Reads are the processor's:
// bilinear in float32 (GameSampling), the frame by the texel under the uv in the compose pass.

cbuffer Cb0Buf : register(b0) { float4 cb0[178]; };
cbuffer Cb1Buf : register(b1) { float4 cb1[5]; };
cbuffer BlitBuf : register(b2)
{
    uint DestW;
    uint DestH;
    uint Reads;          // 0 centre, 1 blur across, 2 blur up and down, 3 cross
    uint PassId;         // which of Beautify's programs
    float TexelX;        // 1 / the source's width and height
    float TexelY;
    uint ComposeReads;   // the compose pass: S0 the frame by the texel under the uv
    uint Unused;
};
cbuffer PresentBuf : register(b3)
{
    int Left;            // the preview's frame on the screen: bottom-left pixel
    int FrameBottom;
    uint WithColour;     // Beautify's compose and URP's grading, or the frame as it is
    uint WithBloom;
    float4 LutParams;    // URP's grading table: 1/width, 1/height, height - 1, exposure
    uint RegionW;        // the part of the screen drawn: the bust's frame (512 square) or the whole screen (the place look)
    uint RegionH;
    uint BloomAlone;     // the place look: with the colour off, the bloom still added (GamePostFast.LookOver)
    float BloomIntensity;
    float4 ViewMap;      // a view of the screen (the place look zoomed): uv = (x * ViewMap.x + ViewMap.y, y * ViewMap.z + ViewMap.w)
    uint ViewMode;       // SrcA is the view (rows from the top), each output pixel its own; SrcB the whole screen's bloom
    uint3 Unused3;
};
Texture2D<float4> SrcA : register(t0);
Texture2D<float4> SrcB : register(t1);
Texture2D<float4> Table : register(t2);              // URP's grading table, rows from the bottom
StructuredBuffer<uint> SrgbBytes : register(t3);     // BackdropRender's linear -> sRGB byte table
RWTexture2D<float4> Dest : register(u0);
RWStructuredBuffer<uint> Output : register(u1);      // RegionW x RegionH BGRA bytes, rows from the top

float4 PointRead(Texture2D<float4> t, float u, float v)
{
    uint w, h; t.GetDimensions(w, h);
    precise float fx = floor(u * (float)w), fy = floor(v * (float)h);
    int x = fx < 0.0f ? 0 : (fx > (float)(w - 1) ? (int)(w - 1) : (int)fx);
    int y = fy < 0.0f ? 0 : (fy > (float)(h - 1) ? (int)(h - 1) : (int)fy);
    return t.Load(int3(x, y, 0));
}

float4 BilinearRead(Texture2D<float4> t, float u, float v)
{
    uint w, h; t.GetDimensions(w, h);
    precise float fx = u * (float)w - 0.5f, fy = v * (float)h - 0.5f;
    precise float x0 = floor(fx), y0 = floor(fy);
    precise float ax = fx - x0, ay = fy - y0;
    int xa = x0 < 0.0f ? 0 : (x0 > (float)(w - 1) ? (int)(w - 1) : (int)x0);
    int xb = x0 + 1.0f < 0.0f ? 0 : (x0 + 1.0f > (float)(w - 1) ? (int)(w - 1) : (int)(x0 + 1.0f));
    int ya = y0 < 0.0f ? 0 : (y0 > (float)(h - 1) ? (int)(h - 1) : (int)y0);
    int yb = y0 + 1.0f < 0.0f ? 0 : (y0 + 1.0f > (float)(h - 1) ? (int)(h - 1) : (int)(y0 + 1.0f));
    float4 p00 = t.Load(int3(xa, ya, 0)), p10 = t.Load(int3(xb, ya, 0)), p01 = t.Load(int3(xa, yb, 0)), p11 = t.Load(int3(xb, yb, 0));
    precise float bx = 1.0f - ax, by = 1.0f - ay;
    precise float4 bottom = p00 * bx + p10 * ax;
    precise float4 top = p01 * bx + p11 * ax;
    precise float4 c = bottom * by + top * ay;
    return c;
}

// A view's own pixel, which the compose reads as the frame (GamePostFast.LookOverView) - in the
// look's own pass only: the bloom's passes share this program and do not set ViewMode (1.9.1:
// a view look's left bound made the next bloom read nothing).
static bool InView = false;
static float4 ViewScene;
float4 S0(float u, float v)
{
    if (InView) return ViewScene;
    return ComposeReads != 0u ? PointRead(SrcA, u, v) : BilinearRead(SrcA, u, v);
}
float4 S1(float u, float v) { return BilinearRead(SrcB, u, v); }
float4 S2(float u, float v) { return float4(0.0f, 0.0f, 0.0f, 0.0f); }
float4 S3(float u, float v) { return float4(0.0f, 0.0f, 0.0f, 0.0f); }

#include "GamePostProgram"

// (float)(Half)x: the mantissa to 10 bits, to nearest, ties to even - as GamePost.Half and
// GamePostFast.HalfRound - by the bits, not the card's f32tof16 (which need not round to nearest:
// measured on the author's card, every bloom value drifted low through the passes).
float Half(float x)
{
    uint b = asuint(x);
    uint a = b & 0x7FFFFFFFu;
    if (a == 0u) return x;
    if (a >= 0x7F800000u) return x;                                  // infinity, NaN
    if (a >= 0x477FF000u) return asfloat((b & 0x80000000u) | 0x7F800000u);   // past the halves' top: infinity
    if (a < 0x38800000u)
    {
        // Below 2^-14: a half's steps are 2^-24; every scaling here is exact.
        precise float q = x * 16777216.0f;
        precise float r = round(q);
        return r * 5.9604644775390625e-8f;
    }
    a = (a + 0x0FFFu + ((a >> 13) & 1u)) & 0xFFFFE000u;
    return asfloat((b & 0x80000000u) | a);
}

[numthreads(8, 8, 1)]
void Blit(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= DestW || id.y >= DestH) return;
    precise float u = ((float)id.x + 0.5f) / (float)DestW;
    precise float v = ((float)id.y + 0.5f) / (float)DestH;
    GameInputs i = (GameInputs)0;
    i.v1x = u; i.v1y = v; i.v1z = u; i.v1w = v;
    if (Reads == 1u)
    {
        precise float t = TexelX * 1.0f;
        i.v1z = -t * 1.3846154f + u;
        precise float a = t * 1.3846154f, c = t * 3.2307692f;
        i.v2z = a + u; i.v2w = 0.0f + v;
        i.v2x = -c + u; i.v2y = -0.0f + v;
        i.v3x = c + u; i.v3y = 0.0f + v;
    }
    else if (Reads == 2u)
    {
        precise float t = TexelY * 1.0f;
        i.v1w = -t * 1.3846154f + v;
        precise float a = t * 1.3846154f, c = t * 3.2307692f;
        i.v2z = 0.0f + u; i.v2w = a + v;
        i.v2x = -0.0f + u; i.v2y = -c + v;
        i.v3x = 0.0f + u; i.v3y = c + v;
    }
    else if (Reads == 3u)
    {
        i.v1w = TexelY + v;
        i.v2z = TexelX + u; i.v2w = 0.0f + v;
        i.v2x = -TexelX + u; i.v2y = -0.0f + v;
        i.v3x = -0.0f + u; i.v3y = -TexelY + v;
    }
    GameRegisters r = (GameRegisters)0;
    [branch] switch (PassId)
    {
        case 0: BloomLuminance(r, i); break;
        case 1: Resample(r, i); break;
        case 2: ResampleAndCombine(r, i); break;
        case 3: FlaresLuminance(r, i); break;
        case 4: FlaresResample(r, i); break;
        case 5: Combine(r, i); break;
        default: Blur(r, i); break;
    }
    Dest[id.xy] = float4(Half(r.o0x), Half(r.o0y), Half(r.o0z), Half(r.o0w));
}

// B10G11R11: no sign, 5-bit exponent, `mantissa` bits, toward zero (GamePost.Small).
float Small(float x, int mantissa)
{
    if (!(x > 0.0f)) return 0.0f;
    uint bits = asuint(x);
    int e = (int)((bits >> 23) & 0xFFu) - 127;
    if (e > 15) return 65024.0f;
    if (e < -14)
    {
        // A step of 2^-14 / 2^m, every operation exact (powers of two).
        float step = asfloat((uint)(127 - 14 - mantissa) << 23);
        float inverse = asfloat((uint)(127 + 14 + mantissa) << 23);
        return floor(x * inverse) * step;
    }
    uint drop = (uint)(23 - mantissa);
    return asfloat(bits & ~((1u << drop) - 1u));
}

float3 Grade(float r, float g, float b)
{
    r = clamp(r, 0.0f, 1.0f); g = clamp(g, 0.0f, 1.0f); b = clamp(b, 0.0f, 1.0f);
    float px = LutParams.x, py = LutParams.y, pz = LutParams.z;
    precise float z = b * pz;
    precise float shift = floor(z);
    precise float ux = r * pz * px + px * 0.5f;
    precise float uy = g * pz * py + py * 0.5f;
    ux += shift * py;
    float4 lo = BilinearRead(Table, ux, uy);
    float4 hi = BilinearRead(Table, ux + py, uy);
    precise float t = z - shift;
    precise float rr = lo.x + (hi.x - lo.x) * t;
    precise float gg = lo.y + (hi.y - lo.y) * t;
    precise float bb = lo.z + (hi.z - lo.z) * t;
    return float3(Small(rr, 6), Small(gg, 6), Small(bb, 5));
}

uint SrgbByte(float light)
{
    if (!(light > 0.0f)) return 0u;
    if (light >= 1.0f) return 255u;
    return SrgbBytes[(uint)(light * 65536.0f)];
}

#define N 512

// The preview's frame: SrcA the frame (the camera's target), SrcB the bloom.
[numthreads(16, 16, 1)]
void Present(uint3 id : SV_DispatchThreadID)
{
    int x = (int)id.x, y = (int)id.y;
    if (x >= (int)RegionW || y >= (int)RegionH) return;
    if (ViewMode != 0u)
    {
        // A view: the picture at this pixel, the bloom and the look at its place on the screen.
        InView = true;
        ViewScene = SrcA.Load(int3(x, y, 0));
        precise float vu = ViewMap.x * ((float)x + 0.5f) + ViewMap.y;
        precise float vv = ViewMap.z * ((float)y + 0.5f) + ViewMap.w;
        float vr, vg, vb;
        if (WithColour != 0u)
        {
            GameInputs vi = (GameInputs)0;
            vi.v1x = vu; vi.v1y = vv;
            GameRegisters vreg = (GameRegisters)0;
            Compose(vreg, vi);
            float3 vgraded = Grade(Small(vreg.o0x, 6), Small(vreg.o0y, 6), Small(vreg.o0z, 5));
            vr = vgraded.x; vg = vgraded.y; vb = vgraded.z;
        }
        else
        {
            vr = ViewScene.x; vg = ViewScene.y; vb = ViewScene.z;
            if (WithBloom != 0u)
            {
                float4 bl = BilinearRead(SrcB, vu, vv);
                vr += bl.x * BloomIntensity; vg += bl.y * BloomIntensity; vb += bl.z * BloomIntensity;
            }
        }
        Output[(uint)(y * (int)RegionW + x)] = SrgbByte(vb) | (SrgbByte(vg) << 8) | (SrgbByte(vr) << 16) | 0xFF000000u;
        return;
    }
    uint fw, fh; SrcA.GetDimensions(fw, fh);
    int sy = FrameBottom + ((int)RegionH - 1 - y), sx = Left + x;
    uint o = (uint)(y * (int)RegionW + x);
    if (sy < 0 || sy >= (int)fh) { Output[o] = 0xFF000000u; return; }
    float r, g, b;
    if (WithColour != 0u)
    {
        GameInputs i = (GameInputs)0;
        precise float u = ((float)sx + 0.5f) / (float)fw;
        precise float v = ((float)sy + 0.5f) / (float)fh;
        i.v1x = u; i.v1y = v;
        GameRegisters reg = (GameRegisters)0;
        Compose(reg, i);
        float3 graded = Grade(Small(reg.o0x, 6), Small(reg.o0y, 6), Small(reg.o0z, 5));
        r = graded.x; g = graded.y; b = graded.z;
    }
    else
    {
        float4 c = SrcA.Load(int3(sx, sy, 0));
        r = c.x; g = c.y; b = c.z;
        if (BloomAlone != 0u && WithBloom != 0u)
        {
            float4 bl = BilinearRead(SrcB, ((float)sx + 0.5f) / (float)fw, ((float)sy + 0.5f) / (float)fh);
            r += bl.x * BloomIntensity; g += bl.y * BloomIntensity; b += bl.z * BloomIntensity;
        }
    }
    Output[o] = SrgbByte(b) | (SrgbByte(g) << 8) | (SrgbByte(r) << 16) | 0xFF000000u;
}
