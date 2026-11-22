// A place's scene drawn by the graphics card (1.10.0): what the place preview's world layer holds -
// the level's art, its objects, its NPCs, their tints and shadows - as WPF draws it, picture by
// picture in the scene's order: premultiplied colour in the screen's own bytes (sRGB), each picture
// over what is under it. Beside it, the lit mask (PlacePreview.WithSilhouettes): every picture's
// silhouette, white where its material is lit and black where it is not.
// GpuPlaceSceneTests holds the picture to the processor's (the preview's own capture).

cbuffer ItemBuf : register(b0)
{
    float4 ClipX;        // where the picture's corner (u, v in 0..1) lands: x = ClipX.x u + ClipX.y v + ClipX.z
    float4 ClipY;
    float4 Tint;         // a tint's colour (as the screen's bytes, straight) and alpha
    float Opacity;
    uint Kind;           // 0 the picture; 1 its alpha filled with Tint
    uint Sampling;       // 0 the nearest texel, 1 between four, 2 between four of its smaller copies
    uint FromBytes;      // the picture is a shader's, still on the card: a buffer of BGRA bytes
    uint BytesW;
    uint BytesH;
    uint Lit;            // in the lit mask: 0 not drawn, 1 lit, 2 unlit
    uint Unused;
    float4 Edge;         // x, y: a pixel of the target in the picture's own u and v - how far past its sides it is drawn
};

Texture2D<float4> Picture : register(t0);     // premultiplied, rows from the top
StructuredBuffer<uint> Bytes : register(t1);  // premultiplied BGRA, rows from the top
SamplerState NearestTexel : register(s0);
SamplerState FourTexels : register(s1);
SamplerState SmallerCopies : register(s2);

struct Corner
{
    float4 pos : SV_Position;
    float2 uv : TEXCOORD0;
};

// Four corners, a strip: (0,0) (1,0) (0,1) (1,1) - each a pixel past the picture's own side, so the pixels its
// edge only partly covers are drawn too (Pixel gives them their share).
Corner Vertex(uint id : SV_VertexID)
{
    float2 c = float2((float)(id & 1u), (float)(id >> 1));
    c = lerp(-Edge.xy, 1.0f + Edge.xy, c);
    Corner o;
    o.pos = float4(ClipX.x * c.x + ClipX.y * c.y + ClipX.z, ClipY.x * c.x + ClipY.y * c.y + ClipY.z, 0.0f, 1.0f);
    o.uv = c;
    return o;
}

float4 ByteAt(int x, int y)
{
    x = clamp(x, 0, (int)BytesW - 1);
    y = clamp(y, 0, (int)BytesH - 1);
    uint p = Bytes[(uint)y * BytesW + (uint)x];
    return float4((float)((p >> 16) & 255u), (float)((p >> 8) & 255u), (float)(p & 255u), (float)(p >> 24)) / 255.0f;
}

float4 Read(float2 uv)
{
    if (FromBytes != 0u)
    {
        float fx = uv.x * (float)BytesW, fy = uv.y * (float)BytesH;
        if (Sampling == 0u) return ByteAt((int)floor(fx), (int)floor(fy));
        fx -= 0.5f; fy -= 0.5f;
        float x0 = floor(fx), y0 = floor(fy);
        float ax = fx - x0, ay = fy - y0;
        float4 top = lerp(ByteAt((int)x0, (int)y0), ByteAt((int)x0 + 1, (int)y0), ax);
        float4 bottom = lerp(ByteAt((int)x0, (int)y0 + 1), ByteAt((int)x0 + 1, (int)y0 + 1), ax);
        return lerp(top, bottom, ay);
    }
    // The nearest texel under the pixel's centre. (WPF works a picture's reading points out in fixed-point steps,
    // and where one falls on the line between two texels - art drawn at a whole number of pixels a texel, an
    // object placed on a half pixel - it can take the other: lines a pixel wide along those texels' sides, at up
    // to 1.3% of a level's pixels, measured over the game's levels. No one offset matches it: a 256th of a
    // pixel along made the Bath the same and the Air Duct worse.)
    if (Sampling == 0u) return Picture.Sample(NearestTexel, uv);
    if (Sampling == 1u) return Picture.Sample(FourTexels, uv);
    return Picture.Sample(SmallerCopies, uv);
}

struct Drawn
{
    float4 colour : SV_Target0;
    float4 lit : SV_Target1;
};

Drawn Pixel(Corner i)
{
    // How much of this pixel the picture's rectangle covers: WPF smooths a picture's own sides so (a turned
    // object's, a level's art where the view goes past it), whatever is inside read as it is.
    float2 perPixel = float2(length(float2(ddx(i.uv.x), ddy(i.uv.x))), length(float2(ddx(i.uv.y), ddy(i.uv.y))));
    float2 inside = min(i.uv, 1.0f - i.uv) / max(perPixel, 1e-12f);
    float cover = saturate(inside.x + 0.5f) * saturate(inside.y + 0.5f);
    float4 p = Read(saturate(i.uv)) * cover;
    Drawn o;
    if (Kind == 1u)
    {
        float a = p.a * Opacity * Tint.a;
        o.colour = float4(Tint.rgb * a, a);
        o.lit = float4(0.0f, 0.0f, 0.0f, 0.0f);
        return o;
    }
    o.colour = p * Opacity;
    float la = p.a * Opacity;
    o.lit = Lit == 1u ? float4(la, la, la, la) : (Lit == 2u ? float4(0.0f, 0.0f, 0.0f, la) : float4(0.0f, 0.0f, 0.0f, 0.0f));
    return o;
}
