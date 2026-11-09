// Darklands Restoration: the picture filters (docs/SETTINGS.md display.filter) as one fragment shader for SDL3's GPU renderer
// (SDL_CreateGPURenderState). Written for this project (GPL-3.0-or-later, as the rest of it); no third-party code. It follows
// src/Darklands.Rendering/PictureFilterReference.cs function by function, and the tests hold it to that reference.
// Compiled by tools/shaders/build_shaders.py (dxc, shader model 6.0) to picture.frag.dxil (Direct3D 12) and picture.frag.spv
// (SPIR-V for Vulkan: Linux, the Steam Deck).
//
// SDL's GPU renderer binds the drawn texture at t0/s0 in space2 and a fragment uniform buffer at b0 in space3; its vertex stage
// gives the draw colour and the texture coordinate as TEXCOORD0 and TEXCOORD1: SDL builds its own shaders through SPIR-V, which
// names every varying TEXCOORDn, and Direct3D 12 links the stages by these names (COLOR0 here: no pipeline).

// Vulkan: SDL binds the texture and its sampler as one combined image sampler at set 2, the uniforms at set 3
[[vk::binding(0, 3)]]
cbuffer Context : register(b0, space3)
{
    float4 sizes;   // the game's picture: width, height (pixels); the picture's rectangle in the window: width, height
    float4 place;   // a window pixel of the picture = v_uv * place.xy - place.zw
    float4 shape;   // whole-number enlargement x, y; mode (0: from the game's picture, 1: the frame already drawn at its size,
                    // the CRT's last step); kind (0 Even, 1 Soft, 2 Soft CRT, 3 HQ, 4 CRT, 5 CRT+)
    float4 crt;     // scanline depth, mask strength, gain, curvature
    float4 soft;    // Soft's spread across and down (game pixels), HQ's "same colour" tolerance, CRT+'s spread across
    float4 plus;    // CRT+: beam height for black and for white (game rows), mask strength, mask gain
    float4 plus2;   // CRT+: beam gain, glow spread (game pixels), glow amount, scanline share (0: rows blended, 1: beams)
    float4 soft2;   // Soft: how far apart two colours may be and still blend (PictureFilterMath.SoftColourSpread); unused
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]]
Texture2D u_texture : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]]
SamplerState u_sampler : register(s0, space2);

struct PSInput
{
    float4 v_color : TEXCOORD0;
    float2 v_uv : TEXCOORD1;
};

struct PSOutput
{
    float4 o_color : SV_Target;
};

static const float PI = 3.14159265f;

// a texel of the game's picture, its edge repeated outside it. Over the canvas (mode 1, CRT+ with an HD pack): the canvas at
// the middle of that game pixel's place (the canvas is drawn with Even, so that is the game pixel's colour, or what an HD
// pack's replacement shows there)
float4 Texel(int2 t)
{
    t = clamp(t, int2(0, 0), int2(sizes.xy) - 1);
    if (shape.z > 0.5)
    {
        float2 at = (float2(t) + 0.5) * sizes.zw / sizes.xy + place.zw;
        return u_texture.Load(int3(int2(floor(at)), 0));
    }
    return u_texture.Load(int3(t, 0));
}

float4 Straight(float4 c)
{
    return c.a > 0.0 ? float4(c.rgb / c.a, c.a) : c;
}

// Even (sharp bilinear): the picture enlarged by whole numbers (hard pixels), sampled bilinearly at p (pixels of the picture's
// rectangle, centres at i + 0.5). An HD pack's overlay: its transparent texels are 0, so the blend is premultiplied: made straight.
float4 Even(float2 p)
{
    float2 k = shape.xy;
    float2 enlarged = sizes.xy * k;
    float2 t = p * enlarged / sizes.zw - 0.5;
    float2 t0 = floor(t);
    float2 w = t - t0;
    int2 a = int2(floor(clamp(t0, 0.0, enlarged - 1.0) / k));
    int2 b = int2(floor(clamp(t0 + 1.0, 0.0, enlarged - 1.0) / k));
    w = float2(a.x == b.x ? 0.0 : w.x, a.y == b.y ? 0.0 : w.y);
    float4 c = lerp(lerp(Texel(int2(a.x, a.y)), Texel(int2(b.x, a.y)), w.x), lerp(Texel(int2(a.x, b.y)), Texel(int2(b.x, b.y)), w.x), w.y);
    return Straight(c);
}

// the texels around (u, v) (texel centres at whole numbers) weighted by a Gaussian of those spreads, normalised
float4 Gauss(float u, float v, float spreadX, float spreadY)
{
    int cx = int(floor(u + 0.5));
    int cy = int(floor(v + 0.5));
    float4 sum = 0.0;
    float total = 0.0;
    for (int j = -2; j <= 2; j++)
    {
        float dy = (cy + j - v) / spreadY;
        for (int i = -2; i <= 2; i++)
        {
            float dx = (cx + i - u) / spreadX;
            float w = exp(-0.5 * (dx * dx + dy * dy));
            float4 c = Texel(int2(cx + i, cy + j));
            sum += float4(c.rgb * c.a, c.a) * w;
            total += w;
        }
    }
    return Straight(sum / total);
}

// Soft: Gauss, each texel weighted as well by how near its colour is to the texel under the point (PictureFilterMath.SoftNearness):
// dithering blends, text and outlines keep their edges
float4 Soft(float2 p)
{
    float u = p.x * sizes.x / sizes.z - 0.5;
    float v = p.y * sizes.y / sizes.w - 0.5;
    int cx = int(floor(u + 0.5));
    int cy = int(floor(v + 0.5));
    float4 centre = Texel(int2(cx, cy));
    float4 sum = 0.0;
    float total = 0.0;
    for (int j = -2; j <= 2; j++)
    {
        float dy = (cy + j - v) / soft.y;
        for (int i = -2; i <= 2; i++)
        {
            float dx = (cx + i - u) / soft.x;
            float4 c = Texel(int2(cx + i, cy + j));
            float3 d = c.rgb - centre.rgb;
            float w = exp(-0.5 * (dx * dx + dy * dy)) * exp(-0.5 * dot(d, d) / (soft2.x * soft2.x));
            sum += float4(c.rgb * c.a, c.a) * w;
            total += w;
        }
    }
    return Straight(sum / total);
}

// the CRT's scanline across the picture's row at py and its aperture column x on a colour
float4 Mask(float4 c, float py, int x)
{
    float rowHeight = sizes.w / sizes.y;
    float v = py / rowHeight;
    float scanline = 1.0 - crt.x * (1.0 + cos(2.0 * PI * (v - floor(v)))) / 2.0;
    int column = x % 3;
    float3 mask = float3(column == 0 ? 1.0 : 1.0 - crt.y, column == 1 ? 1.0 : 1.0 - crt.y, column == 2 ? 1.0 : 1.0 - crt.y);
    return float4(c.rgb * crt.z * scanline * mask, c.a);
}

bool Same(float4 a, float4 b)
{
    return all(abs(a - b) < soft.z);
}

// HQ: Even, with the corner of a texel cut where a staircase edge passes it (PictureFilterReference.Picture.Hq)
float4 Hq(float2 p)
{
    float4 baseColour = Even(p);
    float2 s = p * sizes.xy / sizes.zw;
    int2 t = int2(floor(s));
    float2 f = s - t;
    int dx = f.x >= 0.5 ? 1 : -1;
    int dy = f.y >= 0.5 ? 1 : -1;
    float u = dx > 0 ? f.x : 1.0 - f.x;
    float v = dy > 0 ? f.y : 1.0 - f.y;
    float4 c = Texel(t);
    float4 a = Texel(t + int2(dx, 0));
    float4 b = Texel(t + int2(0, dy));
    float4 q = Texel(t + int2(dx, dy));
    bool cut = Same(a, b) && !Same(c, a) && Same(q, a) && (Same(Texel(t + int2(-dx, dy)), c) || Same(Texel(t + int2(dx, -dy)), c));
    if (!cut)
    {
        return baseColour;
    }
    float ramp = sizes.x / sizes.z + sizes.y / sizes.w;
    float alpha = saturate((u + v - 1.5) / ramp + 0.5);
    return lerp(baseColour, a, alpha);
}

// a row of the picture at u: a narrow Gaussian across, the beam's colour
float4 Row(int row, float u)
{
    int cx = int(floor(u + 0.5));
    float4 sum = 0.0;
    float total = 0.0;
    for (int i = -2; i <= 2; i++)
    {
        float dx = (cx + i - u) / soft.w;
        float w = exp(-0.5 * dx * dx);
        sum += Texel(int2(cx + i, row)) * w;
        total += w;
    }
    return sum / total;
}

float Luma(float4 c)
{
    return dot(c.rgb, float3(0.299, 0.587, 0.114));
}

float4 PlusMask(float4 scan, float4 glow, int x)
{
    int column = x % 3;
    float3 mask = float3(column == 0 ? 1.0 : 1.0 - plus.z, column == 1 ? 1.0 : 1.0 - plus.z, column == 2 ? 1.0 : 1.0 - plus.z);
    return float4(scan.rgb * plus.w * mask + plus2.z * glow.rgb, 1.0);
}

// CRT+: the beams of the rows near the point, each as tall as its brightness makes it; the rows blended where too thin for
// scanlines; the stronger mask; the glow
float4 Plus(float2 p, int x)
{
    float u = p.x * sizes.x / sizes.z - 0.5;
    float v = p.y * sizes.y / sizes.w;
    int r0 = int(floor(v - 0.5));
    float4 beams = 0.0;
    for (int r = r0 - 1; r <= r0 + 2; r++)
    {
        float4 row = Row(clamp(r, 0, int(sizes.y) - 1), u);
        float spread = plus.x + (plus.y - plus.x) * saturate(Luma(row));
        float d = (v - (r + 0.5)) / spread;
        beams += row * exp(-0.5 * d * d);
    }
    beams *= plus2.x;
    float t = v - 0.5 - r0;
    float4 flat = lerp(Row(clamp(r0, 0, int(sizes.y) - 1), u), Row(clamp(r0 + 1, 0, int(sizes.y) - 1), u), t);
    float4 scan = lerp(flat, beams, plus2.w);
    float4 glow = Gauss(u, v - 0.5, plus2.y, plus2.y);
    return PlusMask(scan, glow, x);
}

// the frame drawn into the window-size canvas (an HD pack's replacements in it at their own resolution): a pixel of it
float4 Canvas(float2 seen)
{
    return crt.w > 0.0 ? u_texture.Sample(u_sampler, (seen + place.zw) / place.xy) : u_texture.Load(int3(int2(floor(seen + place.zw)), 0));
}

PSOutput main(PSInput input)
{
    PSOutput output;
    float2 pixel = input.v_uv * place.xy - place.zw;      // window pixel centre, from the picture's top-left corner
    float2 size = sizes.zw;
    int x = int(floor(pixel.x));
    int kind = int(shape.w + 0.5);

    // the curvature (the CRT kinds, off by default): the picture's point seen at this pixel; outside the picture: black
    float2 seen = pixel;
    if (crt.w > 0.0)
    {
        float2 d = pixel / size - 0.5;
        seen = (pixel / size + d * dot(d, d) * crt.w) * size;
        if (seen.x < 0.0 || seen.y < 0.0 || seen.x > size.x || seen.y > size.y)
        {
            output.o_color = float4(0.0, 0.0, 0.0, 1.0);
            return output;
        }
    }

    float4 color;
    if (shape.z < 0.5)
    {
        if (kind == 1)
        {
            color = Soft(seen);
        }
        else if (kind == 2)
        {
            color = Mask(Soft(seen), seen.y, x);
        }
        else if (kind == 3)
        {
            color = Hq(seen);
        }
        else if (kind == 4)
        {
            color = Mask(Even(seen), seen.y, x);
        }
        else if (kind == 5)
        {
            color = Plus(seen, x);
        }
        else
        {
            color = Even(seen);
        }
    }
    else
    {
        color = kind == 5 ? Plus(seen, x) : Mask(Canvas(seen), seen.y, x);
    }
    color.rgb = saturate(color.rgb);
    output.o_color = color * input.v_color;
    return output;
}
