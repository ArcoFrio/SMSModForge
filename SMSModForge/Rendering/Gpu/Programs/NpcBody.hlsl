// A pack NPC over a backdrop on the graphics card (1.9.1): NpcRender.Body's pass - the game's own bust
// program (GameBustProgram, generated; a pack NPC's material is a bust's) at every one of the game's
// pixels in the preview's frame - zoom x zoom of them in each frame pixel - averaged, premultiplied, in
// linear light. The NPC's art covers a rect of its own in the frame (its size in the place), and its
// light is its layer's (the room's): the program's t0, read at the pixel's place on the screen, as
// NpcLight.At reads it on the processor. GpuNpcTests holds the two to each other.
//
// Textures are read as the processor reads them: the art and mask by the texel under the uv (clamped),
// the background and the light texture bilinear in float32.

cbuffer Cb0Buf : register(b0) { float4 cb0[138]; };
cbuffer Cb1Buf : register(b1) { float4 cb1[5]; };
cbuffer NpcBuf : register(b2)
{
    int Left;            // the frame's bottom-left screen pixel
    int FrameBottom;
    int Zoom;
    uint Unused0;
    float ScreenW;
    float ScreenH;
    float NpcLeft;       // the NPC's rect on the screen (pixels from the bottom left)
    float NpcBottom;
    float NpcWidth;
    float NpcHeight;
    float Share;         // 1 / (Zoom * Zoom)
    uint Unused1;
    float4 Tint;         // the vertex colour: linear rgb, alpha
};
Texture2D<float4> Art : register(t0);          // linear rgb (the game's decode), alpha; rows from the bottom
Texture2D<float4> Mask : register(t1);         // as stored; rows from the bottom
Texture2D<float4> Background : register(t2);   // the room the fake GI reads, linear; rows from the bottom
Texture2D<float4> Light : register(t3);        // the room layer's light texture, rgb; rows from the bottom
RWStructuredBuffer<float4> Body : register(u0);   // FrameSize^2, rows from the top

#define N 512

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

float4 S0(float u, float v) { return float4(BilinearRead(Light, u, v).xyz, 1.0f); }   // the room layer's light
float4 S1(float u, float v) { return PointRead(Art, u, v); }
float4 S2(float u, float v) { return PointRead(Mask, u, v); }
float4 S3(float u, float v) { return BilinearRead(Background, u, v); }

#include "GameBustProgram"

// One of the game's pixels: the program's colour there (premultiplied), or none.
float4 Shade(float u, float v, float sx, float sy)
{
    GameInputs i;
    i.v1x = Tint.x; i.v1y = Tint.y; i.v1z = Tint.z; i.v1w = Tint.w;
    i.v2x = u; i.v2y = v;
    i.v2z = sx / ScreenW; i.v2w = sy / ScreenH;      // the light's uv: the pixel's place on the screen
    i.v3x = sx / ScreenW; i.v3y = sy / ScreenH; i.v3z = 0.0f; i.v3w = 1.0f;
    GameRegisters r = (GameRegisters)0;
    Program(r, i);
    if (r.Discarded) return float4(0.0f, 0.0f, 0.0f, 0.0f);
    return float4(r.o0x, r.o0y, r.o0z, r.o0w);
}

[numthreads(16, 16, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    int x = (int)id.x, y = (int)id.y;
    if (x >= N || y >= N) return;
    int sy = FrameBottom + (N - 1 - y);
    precise float4 sum = float4(0.0f, 0.0f, 0.0f, 0.0f);
    int runs = Zoom * Zoom;
    [loop] for (int k = 0; k < runs; k++)
    {
        int sub = k / Zoom, subX = k % Zoom;
        precise float syc = (float)sy + ((float)(Zoom - 1 - sub) + 0.5f) / (float)Zoom;
        precise float sxc = (float)(Left + x) + ((float)subX + 0.5f) / (float)Zoom;
        precise float u = (sxc - NpcLeft) / NpcWidth;
        precise float v = (syc - NpcBottom) / NpcHeight;
        if (!(u >= 0.0f && u < 1.0f && v >= 0.0f && v < 1.0f)) continue;   // off the NPC's quad: nothing drawn
        sum += Shade(u, v, sxc, syc);
    }
    Body[(uint)(y * N + x)] = sum * Share;
}
