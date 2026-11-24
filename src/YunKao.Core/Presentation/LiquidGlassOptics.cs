using System.Numerics;

namespace YunKao.Core.Presentation;

/// <summary>Rounded lens geometry in DIPs, independent of the rendering backend.</summary>
public readonly record struct GlassCorners(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    public GlassCorners(float radius) : this(radius, radius, radius, radius) { }

    public GlassCorners Fit(float width, float height)
    {
        float limit = Math.Max(0, Math.Min(width, height) / 2);
        float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, limit) : 0;
        return new(Clamp(TopLeft), Clamp(TopRight), Clamp(BottomRight), Clamp(BottomLeft));
    }
}

public readonly record struct GlassNormal(Vector2 Direction, float Depth);

/// <summary>Circle-profile bevel: a flat, readable center with refraction confined to the rim.</summary>
public static class LiquidGlassOptics
{
    public static GlassNormal Sample(float x, float y, float width, float height, GlassCorners corners)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        corners = corners.Fit(width, height);
        float px = x - width / 2, py = y - height / 2;
        float radius = py < 0
            ? (px < 0 ? corners.TopLeft : corners.TopRight)
            : (px < 0 ? corners.BottomLeft : corners.BottomRight);
        var q = new Vector2(Math.Abs(px) - width / 2 + radius, Math.Abs(py) - height / 2 + radius);
        var outside = Vector2.Max(q, Vector2.Zero);
        float length = outside.Length();
        float distance = length + Math.Min(Math.Max(q.X, q.Y), 0) - radius;
        Vector2 normal = length > .0001f ? outside / length
            : q.X > q.Y ? Vector2.UnitX : Vector2.UnitY;
        normal *= new Vector2(px < 0 ? -1 : 1, py < 0 ? -1 : 1);
        return new(normal, -distance);
    }

    public static float BevelOffset(float depth, float bevel)
    {
        if (!float.IsFinite(depth) || !float.IsFinite(bevel) || bevel <= 0 || depth >= bevel) return 0;
        float slope = Math.Clamp(1 - depth / bevel, 0, 1);
        return 1 - MathF.Sqrt(Math.Max(0, 1 - slope * slope));
    }

    /// <summary>Bound texture allocations even for large windows and high display scaling.</summary>
    public static float TextureScale(float width, float height, float dpi)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        float scale = float.IsFinite(dpi) && dpi > 0 ? dpi / 96 : 1;
        scale = Math.Min(scale, 2048 / Math.Max(width, height));
        return Math.Min(scale, MathF.Sqrt(1_048_576 / (width * height)));
    }
}
