using ComputeSharp;
using ComputeSharp.D2D1;

namespace YunKao.Controls;

// Add the native rail to the isolated optical source without capturing the
// thumb itself or reading a bitmap back on each animation frame.
[D2DInputCount(1)]
[D2DInputComplex(0)]
[D2DRequiresScenePosition]
[D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
[D2DGeneratedPixelShaderDescriptor]
internal readonly partial struct SwitchTrackShader(
    Float4 toRail, Float2 offset, Float2 railSize, Float4 color, float scale) : ID2D1PixelShader
{
    public Float4 Execute()
    {
        Float2 pixel = D2D.GetScenePosition().XY, p = pixel / scale;
        Float2 point = new(toRail.X * p.X + toRail.Y * p.Y + offset.X,
            toRail.Z * p.X + toRail.W * p.Y + offset.Y);
        float radius = railSize.Y * 15 / 44;
        Float2 half = new(railSize.X * .5f, radius);
        Float2 q = Hlsl.Abs(point - railSize * .5f) - half + radius;
        float d = Hlsl.Min(Hlsl.Max(q.X, q.Y), 0)
            + Hlsl.Length(Hlsl.Max(q, new Float2(0, 0))) - radius;
        float coverage = 1 - Hlsl.SmoothStep(-.5f / scale, .5f / scale, d);
        Float4 background = D2D.SampleInputAtPosition(0, pixel);
        return new Float4(Hlsl.Lerp(background.RGB, color.RGB, coverage), Hlsl.Max(background.A, coverage));
    }
}
