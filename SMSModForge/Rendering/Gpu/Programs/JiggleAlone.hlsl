// A bust or an NPC drawn alone on the graphics card (1.9.1): JiggleShader.Render's pass - the art at
// the uv the jiggle moves it to, tinted, premultiplied, the game's x zoom pixels in each output
// pixel averaged - by the game's own bust program (GameBustProgram, generated), its fake GI off and
// its light white, as the bust over no backdrop has it. The output is the screen's: straight
// colour back to sRGB, premultiplied, as JiggleShader.Colour writes it. GpuAloneTests holds it to
// the processor's.

cbuffer Cb0Buf : register(b0) { float4 cb0[138]; };
cbuffer Cb1Buf : register(b1) { float4 cb1[5]; };
cbuffer AloneBuf : register(b2)
{
    uint OutW;
    uint OutH;
    uint ZoomX;          // the game's pixels across each output pixel (JiggleShader.Sampling.Across)
    uint ZoomY;          // ... and down (Sampling.Down)
    float4 Tint;         // the vertex colour: linear rgb, alpha
    float Share;         // 1 / (ZoomX * ZoomY)
    float3 Unused2;
};
Texture2D<float4> Art : register(t0);     // linear rgb (decoded), alpha; rows from the bottom
Texture2D<float4> Mask : register(t1);    // as stored; rows from the bottom
RWStructuredBuffer<uint> Output : register(u0);   // OutW x OutH BGRA bytes, rows from the top

float4 PointRead(Texture2D<float4> t, float u, float v)
{
    uint w, h; t.GetDimensions(w, h);
    precise float fx = floor(u * (float)w), fy = floor(v * (float)h);
    int x = fx < 0.0f ? 0 : (fx > (float)(w - 1) ? (int)(w - 1) : (int)fx);
    int y = fy < 0.0f ? 0 : (fy > (float)(h - 1) ? (int)(h - 1) : (int)fy);
    return t.Load(int3(x, y, 0));
}

float4 S0(float u, float v) { return float4(1.0f, 1.0f, 1.0f, 1.0f); }
float4 S1(float u, float v) { return PointRead(Art, u, v); }
float4 S2(float u, float v) { return PointRead(Mask, u, v); }
float4 S3(float u, float v) { return float4(0.0f, 0.0f, 0.0f, 1.0f); }

#include "GameBustProgram"

// JiggleShader.Colour's sRGB: linear back to the screen's curve.
float ToSrgb(float c)
{
    c = saturate(c);
    return c <= 0.0031308f ? c * 12.92f : 1.055f * pow(c, 1.0f / 2.4f) - 0.055f;
}

uint Byte(float v) { return (uint)(v * 255.0f + 0.5f); }

[numthreads(16, 16, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= OutW || id.y >= OutH) return;
    uint gw = OutW * ZoomX, gh = OutH * ZoomY;
    precise float4 sum = float4(0.0f, 0.0f, 0.0f, 0.0f);
    uint runs = ZoomX * ZoomY;
    [loop] for (uint k = 0; k < runs; k++)
    {
        uint sub = k / ZoomX, subX = k % ZoomX;
        uint gx = id.x * ZoomX + subX, gy = id.y * ZoomY + sub;
        precise float u = ((float)gx + 0.5f) / (float)gw;
        precise float v = 1.0f - ((float)gy + 0.5f) / (float)gh;
        GameInputs i;
        i.v1x = Tint.x; i.v1y = Tint.y; i.v1z = Tint.z; i.v1w = Tint.w;
        i.v2x = u; i.v2y = v; i.v2z = u; i.v2w = v;
        i.v3x = 0.5f; i.v3y = 0.5f; i.v3z = 0.0f; i.v3w = 1.0f;
        GameRegisters r = (GameRegisters)0;
        Program(r, i);
        if (r.Discarded || !(r.o0w > 0.0f)) continue;
        // Premultiplied linear -> straight -> the screen's curve -> premultiplied again.
        float a = r.o0w;
        sum += float4(ToSrgb(r.o0z / a) * a, ToSrgb(r.o0y / a) * a, ToSrgb(r.o0x / a) * a, a);   // BGRA
    }
    precise float4 p = sum * Share;
    uint o = id.y * OutW + id.x;
    if (p.w <= 0.0f) { Output[o] = 0u; return; }
    Output[o] = Byte(p.x) | (Byte(p.y) << 8) | (Byte(p.z) << 16) | (Byte(p.w) << 24);
}
