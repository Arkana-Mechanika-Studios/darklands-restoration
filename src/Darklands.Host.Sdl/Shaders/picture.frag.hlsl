// Darklands Restoration: the picture filters (docs/SETTINGS.md display.filter) as one fragment shader for SDL3's GPU renderer
// (SDL_CreateGPURenderState). Written for this project (GPL-3.0-or-later, as the rest of it); no third-party code. The arithmetic
// is that of src/Darklands.Rendering/PictureFilterReference.cs and PictureFilterMath.cs, which the tests hold this to.
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
    float4 shape;   // whole-number enlargement x, y; mode (0: sharp bilinear from the game's picture, 1: the picture already
                    // drawn at its size into the drawn texture, mask only); crt (0 or 1)
    float4 crt;     // scanline depth, mask strength, gain, curvature
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

// The picture enlarged by whole numbers (hard pixels), sampled bilinearly at p (pixels of the picture's rectangle, centres at
// i + 0.5): the taps of PictureFilterReference.Taps. The texels are read exactly (Load), so no sampler setting matters.
float4 SharpBilinear(float2 p)
{
    float2 source = sizes.xy;
    float2 k = shape.xy;
    float2 enlarged = source * k;
    float2 t = p * enlarged / sizes.zw - 0.5;
    float2 t0 = floor(t);
    float2 w = t - t0;
    int2 a = int2(floor(clamp(t0, 0.0, enlarged - 1.0) / k));
    int2 b = int2(floor(clamp(t0 + 1.0, 0.0, enlarged - 1.0) / k));
    float4 c00 = u_texture.Load(int3(a.x, a.y, 0));
    float4 c10 = u_texture.Load(int3(b.x, a.y, 0));
    float4 c01 = u_texture.Load(int3(a.x, b.y, 0));
    float4 c11 = u_texture.Load(int3(b.x, b.y, 0));
    return lerp(lerp(c00, c10, w.x), lerp(c01, c11, w.x), w.y);
}

PSOutput main(PSInput input)
{
    PSOutput output;
    float2 pixel = input.v_uv * place.xy - place.zw;      // window pixel centre, from the picture's top-left corner
    float2 size = sizes.zw;

    // the curvature (CRT, off by default): the picture's point seen at this pixel; outside the picture: black
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
        color = SharpBilinear(seen);
        // an HD pack's overlay: transparent texels are 0, so the blend is premultiplied; drawn with alpha blending, straight
        if (color.a > 0.0)
        {
            color.rgb /= color.a;
        }
    }
    else if (crt.w > 0.0)
    {
        color = u_texture.Sample(u_sampler, (seen + place.zw) / place.xy);
    }
    else
    {
        color = u_texture.Load(int3(int2(floor(seen + place.zw)), 0));
    }

    if (shape.w > 0.5)
    {
        // PictureFilterMath.CrtFactors: the scanline across the picture's row, the aperture's column, the gain
        float rowHeight = size.y / sizes.y;
        float v = seen.y / rowHeight;
        float f = v - floor(v);
        float scanline = 1.0 - crt.x * (1.0 + cos(2.0 * PI * f)) / 2.0;
        int column = int(floor(pixel.x)) % 3;
        float3 mask = float3(column == 0 ? 1.0 : 1.0 - crt.y, column == 1 ? 1.0 : 1.0 - crt.y, column == 2 ? 1.0 : 1.0 - crt.y);
        color.rgb = saturate(color.rgb * crt.z * scanline * mask);
    }
    output.o_color = color * input.v_color;
    return output;
}
