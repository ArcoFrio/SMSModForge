// A place's scene, as the card drew it (PlaceScene), into the camera's target (1.10.0): each pixel's
// bytes read as the game's GPU reads sRGB, into the target's 11-11-10-bit format, then the place's own
// lights (PlaceLights.Apply) - the light texture between its four nearest texels where what is drawn
// is lit, each light's glow over it - operation for operation as the processor
// (GamePost.LookOverView). Written for the view (rows from the top, each pixel its own place on the
// screen) or for the whole screen at the game's size (rows from the bottom, for the bloom).
// GpuPlaceSceneTests holds it to the processor's.

cbuffer LitBuf : register(b0)
{
    uint OutW;
    uint OutH;
    uint ScreenRows;     // the whole screen: written rows from the bottom, lit at its own pixels
    uint Masked;         // the lit mask is read (something in the scene is unlit)
    float4 ViewMap;      // a view pixel's centre on the screen: uv = (x ViewMap.x + ViewMap.y, y ViewMap.z + ViewMap.w), v up
    uint WithLights;
    uint LightCount;     // the lights with a glow, in the place's order
    uint TexW;           // the light texture
    uint TexH;
    uint DropCount;      // the NPCs' wet drops over the lit place (WetDrops)
    uint DropSize;       // the particle texture's size (square), and how many copies of it
    uint DropLevels;
    float DropScale;     // this target's pixels to a canvas pixel: which copy a drop is read from
    float CanvasW;       // the canvas (the game's screen, 100 pixels a unit)
    float CanvasH;
    float DropPad0;
    float DropPad1;
};

struct LightRow
{
    float X;             // a round light: where it is on the game's screen, its radius there
    float Y;
    float Radius;
    float Volume;        // its glow's opacity
    float Cos;           // its turn
    float Sin;
    float InnerRadiusMult;
    float InnerAngle;
    float OuterAngle;
    float R;             // its colour times its strength
    float G;
    float B;
    float Falloff;
    uint Shaped;         // 1: a freeform or picture light - its glow is read from ShapedGlow
    uint GlowAt;         // ... starting at this float
    uint Turned;
};

Texture2D<float4> Scene : register(t0);            // premultiplied sRGB bytes, rows from the top
Texture2D<float4> LitMask : register(t1);          // its red: how far what is drawn is lit
StructuredBuffer<float> Decode : register(t2);     // 256: a byte as the game's GPU reads it
StructuredBuffer<float> Texels : register(t3);     // the light texture, RGB a texel, rows from the bottom
Texture2D<float> LookTable : register(t4);         // URP's light lookup: distance
Texture2D<float> AngleTable : register(t5);        // ... and angle
Texture2D<float> FalloffTable : register(t6);      // URP's falloff lookup, 2048 x 128
StructuredBuffer<LightRow> Lights : register(t7);
StructuredBuffer<float> ShapedGlow : register(t8); // each shaped light's glow, RGB a pixel of the game's screen, rows from the bottom

struct DropRow
{
    float X;             // its centre on the canvas, its size there
    float Y;
    float W;
    float H;
    float Alpha;
    float R;             // its colour in linear light (white but for a tint)
    float G;
    float B;
    float Solid;         // 1: a solid quad (the shower's), no texture
    float LitDrop;       // 1: lit with the place (the shower's): blended in before its lights
    float Pad1;
    float Pad2;
};

StructuredBuffer<DropRow> Drops : register(t9);
StructuredBuffer<float> DropAlpha : register(t10); // the particle texture's alpha, each smaller copy after the one before
RWTexture2D<float4> LitTarget : register(u0);

#define SCREEN_W 960
#define SCREEN_H 540

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

int ClampTo(float i, int n) { return i < 0.0f ? 0 : (i > (float)(n - 1) ? n - 1 : (int)i); }

// GameSampling.BilinearOne
float BetweenFour(Texture2D<float> t, int w, int h, float u, float v)
{
    precise float fx = u * (float)w - 0.5f, fy = v * (float)h - 0.5f;
    precise float x0 = floor(fx), y0 = floor(fy);
    precise float ax = fx - x0, ay = fy - y0;
    int xa = ClampTo(x0, w), xb = ClampTo(x0 + 1.0f, w), ya = ClampTo(y0, h), yb = ClampTo(y0 + 1.0f, h);
    precise float bottom = t.Load(int3(xa, ya, 0)) * (1.0f - ax) + t.Load(int3(xb, ya, 0)) * ax;
    precise float top = t.Load(int3(xa, yb, 0)) * (1.0f - ax) + t.Load(int3(xb, yb, 0)) * ax;
    precise float r = bottom * (1.0f - ay) + top * ay;
    return r;
}

// PlaceLights.At: a round light's share at a screen point.
float ShareAt(LightRow l, float sx, float sy)
{
    precise float dx = sx - l.X, dy = sy - l.Y;
    if (l.Turned != 0u)
    {
        precise float tx = l.Cos * dx + l.Sin * dy;
        precise float ty = -l.Sin * dx + l.Cos * dy;
        dx = tx; dy = ty;
    }
    precise float u = (dx / l.Radius + 1.0f) * 0.5f + 0.001953125f;
    precise float v = (dy / l.Radius + 1.0f) * 0.5f + 0.001953125f;
    if (u < 0.0f || u > 1.0f || v < 0.0f || v > 1.0f) return 0.0f;
    precise float att = clamp(BetweenFour(LookTable, 256, 256, u, v) * l.InnerRadiusMult, 0.0f, 1.0f);
    if (l.InnerAngle < 1.0f)
    {
        float g = BetweenFour(AngleTable, 256, 256, u, v);
        precise float num = l.OuterAngle - g, den = l.OuterAngle - l.InnerAngle;
        precise float cone = den > 0.0f ? clamp(num / den, 0.0f, 1.0f) : (num > 0.0f ? 1.0f : 0.0f);
        att = att * cone;
    }
    return BetweenFour(FalloffTable, 2048, 128, att, l.Falloff);
}

// PlaceLights.Apply
float3 Light(float3 c, float sx, float sy, float lit)
{
    precise float u = clamp(sx / (float)SCREEN_W * (float)TexW - 0.5f, 0.0f, (float)(TexW - 1u));
    precise float v = clamp(sy / (float)SCREEN_H * (float)TexH - 0.5f, 0.0f, (float)(TexH - 1u));
    int i = min((int)u, (int)TexW - 2), j = min((int)v, (int)TexH - 2);
    precise float fu = u - (float)i, fv = v - (float)j;
    uint a = (uint)(j * (int)TexW + i) * 3u, b = a + 3u, cc = a + TexW * 3u, d = cc + 3u;
    precise float3 ta = float3(Texels[a], Texels[a + 1u], Texels[a + 2u]);
    precise float3 tb = float3(Texels[b], Texels[b + 1u], Texels[b + 2u]);
    precise float3 tc = float3(Texels[cc], Texels[cc + 1u], Texels[cc + 2u]);
    precise float3 td = float3(Texels[d], Texels[d + 1u], Texels[d + 2u]);
    precise float3 t = (ta + (tb - ta) * fu) * (1.0f - fv) + (tc + (td - tc) * fu) * fv;
    precise float r = c.x, g = c.y, bl = c.z;
    if (lit > 0.0f)
    {
        precise float lr = Small(r * t.x, 6), lg = Small(g * t.y, 6), lb = Small(bl * t.z, 5);
        if (lit >= 1.0f) { r = lr; g = lg; bl = lb; }
        else { r = r + (lr - r) * lit; g = g + (lg - g) * lit; bl = bl + (lb - bl) * lit; }
    }
    precise float gr = 0.0f, gg = 0.0f, gb = 0.0f;
    for (uint k = 0u; k < LightCount; k++)
    {
        LightRow l = Lights[k];
        if (l.Shaped != 0u)
        {
            // Drawn at the game's own screen pixels: the one this point falls in.
            int px = (int)sx, py = (int)sy;
            if (px < 0 || py < 0 || px >= SCREEN_W || py >= SCREEN_H) continue;
            uint o = l.GlowAt + (uint)(py * SCREEN_W + px) * 3u;
            gr += ShapedGlow[o]; gg += ShapedGlow[o + 1u]; gb += ShapedGlow[o + 2u];
            continue;
        }
        precise float f = ShareAt(l, sx, sy) * l.Volume;
        gr += f * l.R; gg += f * l.G; gb += f * l.B;
    }
    if (gr > 0.0f || gg > 0.0f || gb > 0.0f)
    {
        r = Small(r + gr, 6);
        g = Small(g + gg, 6);
        bl = Small(bl + gb, 5);
    }
    return float3(r, g, bl);
}

uint ByteOf(float v) { return (uint)(v * 255.0f + 0.5f); }

// WetDrops.Mips.LevelFor: the copy a picture wide x tall pixels is read from.
uint DropLevel(float wide, float tall)
{
    float t = max((float)DropSize / max(wide, 1e-6f), (float)DropSize / max(tall, 1e-6f));
    if (!(t > 1.0f)) return 0u;
    int level = (int)floor(log2(t) + 0.5f);
    return (uint)clamp(level, 0, (int)DropLevels - 1);
}

// WetDrops.Mips.Sample: between the four nearest texels of a copy.
float DropSample(uint level, float u, float v)
{
    uint at = 0u, s = DropSize;
    for (uint i = 0u; i < level; i++) { at += s * s; s = max(s >> 1, 1u); }
    precise float fx = u * (float)s - 0.5f, fy = v * (float)s - 0.5f;
    precise float x0 = floor(fx), y0 = floor(fy);
    precise float ax = fx - x0, ay = fy - y0;
    int xa = ClampTo(x0, (int)s), xb = ClampTo(x0 + 1.0f, (int)s), ya = ClampTo(y0, (int)s), yb = ClampTo(y0 + 1.0f, (int)s);
    precise float top = DropAlpha[at + (uint)(ya * (int)s + xa)] * (1.0f - ax) + DropAlpha[at + (uint)(ya * (int)s + xb)] * ax;
    precise float bottom = DropAlpha[at + (uint)(yb * (int)s + xa)] * (1.0f - ax) + DropAlpha[at + (uint)(yb * (int)s + xb)] * ax;
    precise float r = top * (1.0f - ay) + bottom * ay;
    return r;
}

// WetDrops.Blend: every drop over canvas point (cx, cy), its colour through its alpha, over what is there.
float3 WithDrops(float3 c, float cx, float cy, float lit)
{
    for (uint i = 0u; i < DropCount; i++)
    {
        DropRow d = Drops[i];
        if (d.LitDrop != lit) continue;
        precise float left = d.X - d.W * 0.5f, top = d.Y - d.H * 0.5f;
        precise float u = (cx - left) / d.W, v = (cy - top) / d.H;
        if (!(u >= 0.0f && u < 1.0f && v >= 0.0f && v < 1.0f)) continue;
        precise float a = d.Alpha;
        if (d.Solid < 0.5f) a *= DropSample(DropLevel(d.W * DropScale, d.H * DropScale), u, v);
        if (!(a > 0.0f)) continue;
        c = float3(Small(c.x + (d.R - c.x) * a, 6), Small(c.y + (d.G - c.y) * a, 6), Small(c.z + (d.B - c.z) * a, 5));
    }
    return c;
}

[numthreads(16, 16, 1)]
void Lit(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= OutW || id.y >= OutH) return;
    int x = (int)id.x, y = (int)id.y;
    int row = ScreenRows != 0u ? (int)OutH - 1 - y : y;
    float4 s = Scene.Load(int3(x, row, 0));
    float3 c = float3(Small(Decode[ByteOf(s.x)], 6), Small(Decode[ByteOf(s.y)], 6), Small(Decode[ByteOf(s.z)], 5));
    // WetDrops.Over: Screen (rows from the bottom) and View - where on the canvas this pixel's centre is.
    precise float cx = 0.0f, cy = 0.0f;
    if (DropCount != 0u)
    {
        if (ScreenRows != 0u)
        {
            cx = ((float)x + 0.5f) * CanvasW / (float)OutW;
            cy = CanvasH - ((float)y + 0.5f) * CanvasH / (float)OutH;
        }
        else
        {
            precise float du = ViewMap.x * ((float)x + 0.5f) + ViewMap.y;
            precise float dv = ViewMap.z * ((float)y + 0.5f) + ViewMap.w;
            cx = du * CanvasW;
            cy = (1.0f - dv) * CanvasH;
        }
        // A shower's drops: lit with the place, so into it before its lights.
        c = WithDrops(c, cx, cy, 1.0f);
    }
    if (WithLights != 0u)
    {
        precise float sx, sy;
        if (ScreenRows != 0u)
        {
            sx = ((float)x + 0.5f) * (float)SCREEN_W / (float)OutW;
            sy = ((float)y + 0.5f) * (float)SCREEN_H / (float)OutH;
        }
        else
        {
            precise float vu = ViewMap.x * ((float)x + 0.5f) + ViewMap.y;
            precise float vv = ViewMap.z * ((float)y + 0.5f) + ViewMap.w;
            sx = vu * (float)SCREEN_W;
            sy = vv * (float)SCREEN_H;
        }
        float lit = Masked != 0u ? (float)ByteOf(LitMask.Load(int3(x, row, 0)).x) / 255.0f : 1.0f;
        c = Light(c, sx, sy, lit);
    }
    // The towel's drops: unlit, over the lit place.
    if (DropCount != 0u) c = WithDrops(c, cx, cy, 0.0f);
    LitTarget[id.xy] = float4(c, 1.0f);
}
