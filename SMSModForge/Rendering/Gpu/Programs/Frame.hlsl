// The bust preview's frame over a backdrop with a lamp, on the graphics card (1.9.1): the room lit
// through the bust's shadow and the edge glow's background (GameLamp.Room), a bust layer or a face
// blended into the camera's target (BackdropRender.Layer, OverlayLayer), and the lamp's glow over
// everything (GameLamp.Glow) - each the processor's, operation for operation. GpuFrameTests holds
// the frame to the processor's.
//
// Screen images are rows from the bottom; the preview's frame (its layers, coverage) rows from the
// top, as on the processor.

cbuffer FrameBuf : register(b0)
{
    int ScreenW;
    int ScreenH;
    int TexW;            // the light texture (the shadow's size)
    int TexH;
    int Left;            // the preview's frame on the screen: bottom-left pixel
    int FrameBottom;
    int BustBottom;      // where the bust's art rect starts
    uint WithShadow;
    float GlobalR;
    float GlobalG;
    float GlobalB;
    float Intensity;     // the lamp's shadow intensity
    float VolumeIntensity;
    int BackgroundW;
    int BackgroundH;
    uint Unused;
};

StructuredBuffer<float> Shadow : register(t0);       // TexW x TexH, rows from the bottom
StructuredBuffer<float> LampTexels : register(t1);   // RGB a texel
StructuredBuffer<float> Albedo : register(t2);       // RGB a screen pixel
StructuredBuffer<float> NoGlow : register(t3);
StructuredBuffer<float> Room : register(t4);
StructuredBuffer<float> Glow : register(t5);
StructuredBuffer<float4> Layer : register(t6);       // FrameSize^2, premultiplied, rows from the top
Texture2D<float4> Overlay : register(t7);            // a face, decoded: rgb linear, alpha; rows from the top, 256 square
StructuredBuffer<float> LightFrame : register(t8);   // a backdrop without a lamp: what its lights add to a bust, RGB a frame pixel, rows from the top

RWStructuredBuffer<float4> Texels : register(u0);    // the light texture through the shadow
RWStructuredBuffer<float4> Target : register(u1);    // the camera's target, ScreenW x ScreenH (a buffer: every card can read one it writes)
RWTexture2D<float4> Background : register(u2);       // the edge glow's background
RWStructuredBuffer<float> Coverage : register(u3);   // FrameSize^2, rows from the top
RWTexture2D<float4> FrameOut : register(u4);         // the target as the post-processing reads it

#define N 512
#define S 256

// B10G11R11: no sign, 5-bit exponent, `mantissa` bits, toward zero (GamePost.Small).
float Small(float x, int mantissa)
{
    if (!(x > 0.0f)) return 0.0f;
    uint bits = asuint(x);
    int e = (int)((bits >> 23) & 0xFFu) - 127;
    if (e > 15) return 65024.0f;
    if (e < -14)
    {
        float step = asfloat((uint)(127 - 14 - mantissa) << 23);
        float inverse = asfloat((uint)(127 + 14 + mantissa) << 23);
        return floor(x * inverse) * step;
    }
    uint drop = (uint)(23 - mantissa);
    return asfloat(bits & ~((1u << drop) - 1u));
}

float3 ToFrame(float3 c) { return float3(Small(c.x, 6), Small(c.y, 6), Small(c.z, 5)); }

// GameLamp.LightTexture
[numthreads(64, 1, 1)]
void LightTexture(uint3 id : SV_DispatchThreadID)
{
    uint k = id.x;
    if (k >= (uint)(TexW * TexH)) return;
    precise float s = WithShadow != 0u ? Shadow[k] : 0.0f;
    precise float keep = 1.0f - s;
    precise float dim = (1.0f - Intensity) * s;
    uint o = k * 3u;
    precise float r = GlobalR + (LampTexels[o] * keep + LampTexels[o] * dim);
    precise float g = GlobalG + (LampTexels[o + 1u] * keep + LampTexels[o + 1u] * dim);
    precise float b = GlobalB + (LampTexels[o + 2u] * keep + LampTexels[o + 2u] * dim);
    Texels[k] = float4(Small(r, 6), Small(g, 6), Small(b, 5), 0.0f);
}

// GameLamp.Between: the light texture at a screen pixel's centre, between its four nearest texels.
float3 Between(int sx, int sy)
{
    precise float u = clamp(((float)sx + 0.5f) / (float)ScreenW * (float)TexW - 0.5f, 0.0f, (float)(TexW - 1));
    precise float v = clamp(((float)sy + 0.5f) / (float)ScreenH * (float)TexH - 0.5f, 0.0f, (float)(TexH - 1));
    int i = min((int)u, TexW - 2), j = min((int)v, TexH - 2);
    precise float fu = u - (float)i, fv = v - (float)j;
    float3 a = Texels[j * TexW + i].xyz, b = Texels[j * TexW + i + 1].xyz, c = Texels[(j + 1) * TexW + i].xyz, d = Texels[(j + 1) * TexW + i + 1].xyz;
    precise float3 r = (a + (b - a) * fu) * (1.0f - fv) + (c + (d - c) * fu) * fv;
    return r;
}

// GameLamp.Room: the room lit through the shadow, without the glow.
[numthreads(16, 16, 1)]
void RoomPass(uint3 id : SV_DispatchThreadID)
{
    int x = (int)id.x, y = (int)id.y;
    if (x >= ScreenW || y >= ScreenH) return;
    uint p = (uint)(y * ScreenW + x) * 3u;
    bool shaded = false;
    if (WithShadow != 0u)
    {
        precise float v = clamp(((float)y + 0.5f) / (float)ScreenH * (float)TexH - 0.5f, 0.0f, (float)(TexH - 1));
        int j = min((int)v, TexH - 2);
        precise float u = clamp(((float)x + 0.5f) / (float)ScreenW * (float)TexW - 0.5f, 0.0f, (float)(TexW - 1));
        int i = min((int)u, TexW - 2);
        int k = j * TexW + i;
        shaded = Shadow[k] > 0.0f || Shadow[k + 1] > 0.0f || Shadow[k + TexW] > 0.0f || Shadow[k + TexW + 1] > 0.0f;
    }
    if (!shaded)
    {
        Target[y * ScreenW + x] = float4(NoGlow[p], NoGlow[p + 1u], NoGlow[p + 2u], 1.0f);
        return;
    }
    float3 l = Between(x, y);
    precise float r = Albedo[p] * l.x, g = Albedo[p + 1u] * l.y, b = Albedo[p + 2u] * l.z;
    Target[y * ScreenW + x] = float4(Small(r, 6), Small(g, 6), Small(b, 5), 1.0f);
}

// GameLamp.Room's copy for the edge glow: one pixel of every 4x4, the one at (1, 1).
[numthreads(16, 16, 1)]
void BackgroundPass(uint3 id : SV_DispatchThreadID)
{
    int i = (int)id.x, j = (int)id.y;
    if (i >= BackgroundW || j >= BackgroundH) return;
    float4 c = Target[(j * 4 + 1) * ScreenW + i * 4 + 1];
    Background[int2(i, j)] = float4(c.xyz, 1.0f);
}

[numthreads(16, 16, 1)]
void ClearCoverage(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= N || id.y >= N) return;
    Coverage[id.y * N + id.x] = 0.0f;
}

// BackdropRender.Layer: a premultiplied layer of the bust over the target.
[numthreads(16, 16, 1)]
void LayerPass(uint3 id : SV_DispatchThreadID)
{
    int x = (int)id.x, y = (int)id.y;
    if (x >= N || y >= N) return;
    int sy = FrameBottom + (N - 1 - y);
    if (sy < 0 || sy >= ScreenH) return;
    uint i = (uint)(y * N + x);
    float4 l = Layer[i];
    float a = l.w;
    if (a <= 0.0f) return;
    int o = sy * ScreenW + Left + x;
    float4 t = Target[o];
    precise float keep = 1.0f - a;
    precise float3 c = l.xyz + t.xyz * keep;
    Target[o] = float4(ToFrame(c), 1.0f);
    precise float cov = a + Coverage[i] * keep;
    Coverage[i] = cov;
}

// BackdropRender.OverlayLayer: a face, two screen pixels a texel, nearest, at the bust's height.
[numthreads(16, 16, 1)]
void OverlayPass(uint3 id : SV_DispatchThreadID)
{
    int x = (int)id.x, y = (int)id.y;
    if (x >= N || y >= N) return;
    int sy = FrameBottom + (N - 1 - y);
    int up = sy - BustBottom;
    if (up < 0 || up >= N || sy < 0 || sy >= ScreenH) return;
    float4 f = Overlay.Load(int3(x / 2, S - 1 - up / 2, 0));
    float a = f.w;
    if (a <= 0.0f) return;
    int o = sy * ScreenW + Left + x;
    float4 t = Target[o];
    precise float keep = 1.0f - a;
    precise float3 c = f.xyz * a + t.xyz * keep;
    Target[o] = float4(ToFrame(c), 1.0f);
    uint i = (uint)(y * N + x);
    precise float cov = a + Coverage[i] * keep;
    Coverage[i] = cov;
}

// BustShadow.At: the shadow at a screen pixel, between its four nearest texels.
float ShadowAt(int sx, int sy)
{
    precise float u = ((float)sx + 0.5f) / (float)ScreenW * (float)TexW - 0.5f;
    precise float v = ((float)sy + 0.5f) / (float)ScreenH * (float)TexH - 0.5f;
    u = clamp(u, 0.0f, (float)(TexW - 1)); v = clamp(v, 0.0f, (float)(TexH - 1));
    int i = min((int)u, TexW - 2), j = min((int)v, TexH - 2);
    precise float fu = u - (float)i, fv = v - (float)j;
    float a = Shadow[j * TexW + i], b = Shadow[j * TexW + i + 1], c = Shadow[(j + 1) * TexW + i], d = Shadow[(j + 1) * TexW + i + 1];
    precise float r = (a + (b - a) * fu) * (1.0f - fv) + (c + (d - c) * fu) * fv;
    return r;
}

// GameLamp.Glow: the lamp's glow over everything drawn, through the shadow.
[numthreads(16, 16, 1)]
void GlowPass(uint3 id : SV_DispatchThreadID)
{
    int x = (int)id.x, y = (int)id.y;
    if (x >= ScreenW || y >= ScreenH) return;
    uint p = (uint)(y * ScreenW + x) * 3u;
    float s = WithShadow != 0u ? ShadowAt(x, y) : 0.0f;
    int fx = x - Left, fy = N - 1 - (y - FrameBottom);
    bool drawnOver = fx >= 0 && fx < N && fy >= 0 && fy < N && Coverage[fy * N + fx] > 0.0f;
    int o = y * ScreenW + x;
    float4 t = Target[o];
    if (s == 0.0f && !drawnOver)
    {
        Target[o] = float4(Room[p], Room[p + 1u], Room[p + 2u], t.w);
        return;
    }
    precise float keep = 1.0f - s;
    precise float dim = (1.0f - VolumeIntensity) * s;
    precise float r = t.x + (Glow[p] * keep + Glow[p] * dim);
    precise float g = t.y + (Glow[p + 1u] * keep + Glow[p + 1u] * dim);
    precise float b = t.z + (Glow[p + 2u] * keep + Glow[p + 2u] * dim);
    Target[o] = float4(Small(r, 6), Small(g, 6), Small(b, 5), t.w);
}

// The target into the texture the post-processing reads.
[numthreads(16, 16, 1)]
void CopyOut(uint3 id : SV_DispatchThreadID)
{
    int x = (int)id.x, y = (int)id.y;
    if (x >= ScreenW || y >= ScreenH) return;
    FrameOut[int2(x, y)] = Target[y * ScreenW + x];
}

// BackdropRender.Lamps: a backdrop without a lamp - what its lights add to the bust where it covers.
[numthreads(16, 16, 1)]
void LampsAddPass(uint3 id : SV_DispatchThreadID)
{
    int x = (int)id.x, y = (int)id.y;
    if (x >= N || y >= N) return;
    int sy = FrameBottom + (N - 1 - y);
    if (sy < 0 || sy >= ScreenH) return;
    uint c = (uint)(y * N + x);
    float a = Coverage[c];
    if (a <= 0.0f) return;
    uint j = c * 3u;
    int o = sy * ScreenW + Left + x;
    float4 t = Target[o];
    precise float r = t.x + LightFrame[j] * a;
    precise float g = t.y + LightFrame[j + 1u] * a;
    precise float b = t.z + LightFrame[j + 2u] * a;
    Target[o] = float4(ToFrame(float3(r, g, b)), 1.0f);
}
