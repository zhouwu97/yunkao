using System.Numerics;

namespace YunKao.Core.Presentation;

/// <summary>The clear moving lens used by both the lab and application navigation.</summary>
public static class LiquidLensPreset
{
    public const float Blur = .6f;
    public const float Refraction = 14;
    public const float Dispersion = .5f;
    public const float Sheen = .85f;
    public const float Saturation = 1;
    public const float PressedBlurReduction = .8f;
    public const float WidthExpansion = .025f;
    public const float HeightExpansion = .39f;
    public static Vector4 Tint => new(1, 1, 1, .03f);
    public static Vector4 DarkTint => new(.08f, .09f, .14f, .04f);
}
