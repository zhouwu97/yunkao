using ComputeSharp;
using ComputeSharp.D2D1;

namespace YunKao.Controls;

// Optical equations adapted from zhouwu97/FlutterLiquidglass, revision 2c281279.
// Original circle-profile/spectral model: Kyant0/AndroidLiquidGlass (Apache-2.0).
// Deformed SDF and directional lighting: design-systems (MIT). See Assets/GlassNotices.
[D2DInputCount(2)]
[D2DInputComplex(0)]
[D2DInputComplex(1)]
[D2DRequiresScenePosition]
[D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
[D2DGeneratedPixelShaderDescriptor]
internal readonly partial struct LiquidGlassShader(
    Float2 size, Float2 center, Float4 corners, Float2 pointer, Float4 inverse,
    Float2 bend, Float4 tint, float pressure, float refraction, float dispersion,
    float energy, float dpiScale, float sheen, float foreground, float compare, float anchored = 0, Float2 grab = default,
    float interiorEffects = 1) : ID2D1PixelShader
{
    private float Distance(Float2 point)
    {
        Float2 p = point - center;
        Float2 half = size * .5f;
        if (anchored > .5f && anchored < 1.5f)
        {
            // Compact support around the grabbed edge. No whole-panel affine transform:
            // distant corners, opposite edges, the body and its texture stay in place.
            Float2 delta = p - (grab - .5f) * size;
            float radius = Hlsl.Clamp(Hlsl.Min(size.X, size.Y) * .65f, 32, 144);
            float weight = Hlsl.Saturate(1 - Hlsl.Dot(delta, delta) / (radius * radius));
            weight = weight * weight * (3 - 2 * weight);
            Float2 uv = Hlsl.Abs(p / Hlsl.Max(half, new Float2(1, 1)));
            float edge = Hlsl.Saturate((Hlsl.Max(uv.X, uv.Y) - .35f) / .65f);
            p -= bend * weight * edge * edge;
        }
        else
        {
            if (anchored < 1.5f)
                p = new Float2(inverse.X * p.X + inverse.Y * p.Y, inverse.Z * p.X + inverse.W * p.Y);
            Float2 uv = Hlsl.Clamp(p / Hlsl.Max(half, new Float2(1, 1)), new Float2(-1.3f, -1.3f), new Float2(1.3f, 1.3f));
            p -= bend * new Float2(1 - uv.Y * uv.Y, 1 - uv.X * uv.X) * .7f;
        }
        float r = p.Y < 0 ? (p.X < 0 ? corners.X : corners.Y) : (p.X < 0 ? corners.W : corners.Z);
        r = Hlsl.Min(r, Hlsl.Min(half.X, half.Y));
        Float2 q = Hlsl.Abs(p) - half + r;
        return Hlsl.Min(Hlsl.Max(q.X, q.Y), 0) + Hlsl.Length(Hlsl.Max(q, new Float2(0, 0))) - r;
    }

    private Float2 Normal(Float2 p)
    {
        const float e = .35f;
        Float2 n = new(Distance(p + new Float2(e, 0)) - Distance(p - new Float2(e, 0)),
            Distance(p + new Float2(0, e)) - Distance(p - new Float2(0, e)));
        return n / Hlsl.Max(Hlsl.Length(n), .0001f);
    }

    private Float3 Sample(Float2 p)
    {
        Float4 sample = D2D.SampleInputAtPosition(0, p * dpiScale);
        Float3 color = Hlsl.Lerp(sample.RGB, tint.RGB, tint.A);
        if (foreground > .5f)
        {
            Float2 at = center + (p - center) / (1 + .20f * pressure);
            Float4 ink = D2D.SampleInputAtPosition(1, at * dpiScale);
            color = ink.RGB + color * (1 - ink.A);
        }
        return color;
    }

    private float Light(float d, Float2 n)
    {
        Float2 direction = new Float2(-.65f, -1) + (pointer - center) / Hlsl.Max(size.X, 1) * .8f;
        direction /= Hlsl.Max(Hlsl.Length(direction), .0001f);
        float a = Hlsl.Pow(Hlsl.Max(Hlsl.Dot(n, direction), 0), 7);
        float b = Hlsl.Pow(Hlsl.Max(Hlsl.Dot(n, -direction), 0), 7) * .52f;
        float edge = Hlsl.Saturate(1 + d / 8);
        float slope = Hlsl.Pow(edge, 1.65f) * .94f;
        float nz = Hlsl.Sqrt(Hlsl.Max(1 - slope * slope, .001f));
        float fresnel = .04f + .96f * Hlsl.Pow(1 - nz, 5);
        float outline = Hlsl.Exp(-Hlsl.Pow((d + .7f) / .65f, 2));
        float bloom = Hlsl.Exp(-Hlsl.Pow((d + 1.4f) / 2, 2));
        float caustic = Hlsl.Exp(-Hlsl.Pow((d + 3) / 1.65f, 2));
        return (outline * (.14f + a * .62f + b * .32f) + bloom * (.025f + a + b) * .12f
            + caustic * b * .07f + fresnel * .045f) * sheen * (1 - compare);
    }

    public Float4 Execute()
    {
        Float2 p = D2D.GetScenePosition().XY / dpiScale;
        float d = Distance(p);
        Float2 n = Normal(p);
        if (anchored > 1.5f)
        {
            Float2 worldNormal = new(inverse.X * n.X + inverse.Z * n.Y, inverse.Y * n.X + inverse.W * n.Y);
            float magnitude = Hlsl.Max(Hlsl.Length(worldNormal), .0001f);
            n = worldNormal / magnitude;
            d /= magnitude;
        }
        float mask = 1 - Hlsl.SmoothStep(-.65f / dpiScale, .65f / dpiScale, d);
        if (mask < .0001f) return new Float4(0, 0, 0, 0);
        Float2 q = p - center;
        Float2 radial = q / Hlsl.Max(Hlsl.Length(q), .0001f);
        Float2 opticalNormal = n + radial * foreground;
        opticalNormal /= Hlsl.Max(Hlsl.Length(opticalNormal), .0001f);
        // Keep the lens' curved optical footprint visible on large cards as well as capsules.
        float height = anchored > 1.5f ? Hlsl.Clamp(Hlsl.Min(size.X, size.Y) * .22f, 12, 40)
            : Hlsl.Min(16, Hlsl.Min(size.X, size.Y) * .30f);
        float band = Hlsl.Saturate(1 + d / Hlsl.Max(height, .001f));
        float circle = 1 - Hlsl.Sqrt(Hlsl.Max(0, 1 - band * band));
        Float2 offset = -opticalNormal * circle * refraction * (1 + energy * .12f) * (1 - compare);
        if (anchored > 1.5f)
            offset = new Float2(inverse.X * offset.X + inverse.Y * offset.Y, inverse.Z * offset.X + inverse.W * offset.Y);
        Float2 at = p + offset;
        float spread = dispersion * q.X * q.Y / Hlsl.Max(size.X * size.Y * .25f, 1);
        Float2 split = offset * spread;
        // Keep spectral fringes within a small optical footprint, particularly on sharp text.
        split *= Hlsl.Min(1, 2.5f / Hlsl.Max(Hlsl.Length(split), .0001f));
        Float3 r = Sample(at + split);
        Float3 o = Sample(at + split * (2f / 3));
        Float3 y = Sample(at + split * (1f / 3));
        Float3 g = Sample(at);
        Float3 cyan = Sample(at - split * (1f / 3));
        Float3 b = Sample(at - split * (2f / 3));
        Float3 v = Sample(at - split);
        Float3 color = new((r.R + o.R + y.R) / 3.5f + v.R / 7,
            o.G / 7 + (y.G + g.G + cyan.G) / 3.5f, (cyan.B + b.B + v.B) / 3);
        color += Light(d, n);
        float glint = Hlsl.Exp(-Hlsl.Length((p - pointer) / new Float2(90, 70)) * 2);
        color += glint * .035f * pressure * interiorEffects * (1 - compare);
        return new Float4(Hlsl.Saturate(color) * mask, mask);
    }
}
