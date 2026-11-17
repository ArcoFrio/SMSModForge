// A level's art moved by its mask on the graphics card (1.9.1): MilkingShader.Render (the level
// material's _JIGGLETYPE_MILKING variant, as ported from the game's bytecode), operation for
// operation, the art read as the processor reads it - its bytes, bilinear, rows from the top.
// GpuMilkingTests holds it to the processor's.

cbuffer MilkingBuf : register(b0)
{
    uint SrcW;
    uint SrcH;
    uint MaskW;
    uint MaskH;
    uint OutW;
    uint OutH;
    float TimePhase;     // _Time.y * speed + phase offset
    float Variation;
    float Strength;
    float WaveCount;
    float Compression;
    float DirX;          // normalised as the processor normalises it
    float DirY;
    float3 Unused;
};
Texture2D<uint4> Art : register(t0);    // BGRA bytes, rows from the top
Texture2D<uint4> Mask : register(t1);   // BGRA bytes, rows from the top
RWStructuredBuffer<uint> Output : register(u0);

#define PI 3.14159f

void WavesAt(float t, out float slide, out float cross1, out float cross2)
{
    precise float waveArg = t * WaveCount * PI + -(TimePhase + TimePhase);
    slide = sin(t * WaveCount + TimePhase);
    cross1 = sin(waveArg);
    precise float a2 = waveArg * 1.7f + TimePhase * 0.8f;
    cross2 = sin(a2);
}

[numthreads(16, 16, 1)]
void Main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= OutW || id.y >= OutH) return;
    precise float v = 1.0f - ((float)id.y + 0.5f) / (float)OutH;
    precise float u = ((float)id.x + 0.5f) / (float)OutW;
    int mx = clamp((int)floor(u * (float)MaskW), 0, (int)MaskW - 1);
    int my = clamp((int)floor(v * (float)MaskH), 0, (int)MaskH - 1);
    float maskA = (float)Mask.Load(int3(mx, (int)MaskH - 1 - my, 0)).w / 255.0f;
    precise float amp = (Variation * sin(u * PI) + 1.0f) * (maskA * Strength);

    float sinSlide, sinCross1, sinCross2;
    // dot(uv, dir) as the processor forms it: by row, by column, or both.
    precise float t = DirX == 0.0f ? (0.0f * DirX + v * DirY) : (DirY == 0.0f ? (u * DirX + 0.0f * DirY) : (u * DirX + v * DirY));
    WavesAt(t, sinSlide, sinCross1, sinCross2);

    precise float c1 = amp * sinCross1 * Compression;
    precise float slide = amp * sinSlide * 0.5f;
    precise float offU = -DirY * c1 + slide * DirX;
    precise float offV = DirX * c1 + slide * DirY;
    precise float c2 = sinCross2 * 0.4f;
    offU = -DirY * c2 * amp + offU;
    offV = DirX * c2 * amp + offV;
    precise float su = clamp(u + offU, 0.0f, 1.0f);
    precise float sv = clamp(v + offV, 0.0f, 1.0f);

    // MilkingShader.SampleBilinear: rows top-down, so the game's v turned over.
    precise float fx = su * (float)SrcW - 0.5f, fy = (1.0f - sv) * (float)SrcH - 0.5f;
    int x0 = (int)floor(fx), y0 = (int)floor(fy);
    precise float ax = fx - (float)x0, ay = fy - (float)y0;
    int x1 = min((int)SrcW - 1, x0 + 1), y1 = min((int)SrcH - 1, y0 + 1);
    x0 = clamp(x0, 0, (int)SrcW - 1); y0 = clamp(y0, 0, (int)SrcH - 1);
    float4 p00 = (float4)Art.Load(int3(x0, y0, 0)), p01 = (float4)Art.Load(int3(x1, y0, 0));
    float4 p10 = (float4)Art.Load(int3(x0, y1, 0)), p11 = (float4)Art.Load(int3(x1, y1, 0));
    precise float4 top = p00 + (p01 - p00) * ax;
    precise float4 bot = p10 + (p11 - p10) * ax;
    precise float4 c = top + (bot - top) * ay + 0.5f;
    uint4 b = (uint4)c;
    Output[id.y * OutW + id.x] = b.x | (b.y << 8) | (b.z << 16) | (b.w << 24);
}
