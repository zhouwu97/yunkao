using System.Numerics;
using YunKao.Core.Presentation;

namespace YunKao.Tests;

public sealed class LiquidGlassOpticsTests
{
    [Fact]
    public void Center_is_flat_and_edges_refract_in_opposite_directions()
    {
        var corners = new GlassCorners(16);
        var center = LiquidGlassOptics.Sample(100, 40, 200, 80, corners);
        Assert.Equal(0, LiquidGlassOptics.BevelOffset(center.Depth, 8));
        var left = LiquidGlassOptics.Sample(1, 40, 200, 80, corners);
        var right = LiquidGlassOptics.Sample(199, 40, 200, 80, corners);
        Assert.Equal(-Vector2.UnitX, left.Direction);
        Assert.Equal(Vector2.UnitX, right.Direction);
        Assert.Equal(left.Depth, right.Depth);
        Assert.InRange(LiquidGlassOptics.BevelOffset(left.Depth, 8), 0.1f, 1);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, .1339746f)]
    [InlineData(8, 0)]
    [InlineData(40, 0)]
    public void Bevel_matches_a_circle_profile(float depth, float expected)
        => Assert.Equal(expected, LiquidGlassOptics.BevelOffset(depth, 8), .00001f);

    [Fact]
    public void Four_corners_use_their_own_geometry()
    {
        var corners = new GlassCorners(0, 20, 10, 0);
        Assert.True(LiquidGlassOptics.Sample(1, 1, 100, 60, corners).Depth > 0);
        Assert.True(LiquidGlassOptics.Sample(99, 1, 100, 60, corners).Depth < 0);
        Assert.True(LiquidGlassOptics.Sample(99, 59, 100, 60, corners).Depth < 0);
        Assert.True(LiquidGlassOptics.Sample(1, 59, 100, 60, corners).Depth > 0);
        var fitted = new GlassCorners(float.NaN, -1, 400, float.PositiveInfinity).Fit(40, 20);
        Assert.Equal(new GlassCorners(0, 0, 10, 0), fitted);
    }

    [Theory]
    [InlineData(200, 80, 96)]
    [InlineData(200, 80, 144)]
    [InlineData(4000, 3000, 288)]
    [InlineData(8000, 6000, 384)]
    public void Texture_allocations_are_bounded_at_high_dpi(float width, float height, float dpi)
    {
        float scale = LiquidGlassOptics.TextureScale(width, height, dpi);
        Assert.InRange(scale, float.Epsilon, dpi / 96);
        Assert.True(Math.Max(width, height) * scale <= 2048.01f);
        Assert.True(width * scale * height * scale <= 1_048_577);
        if (width == 200) Assert.Equal(dpi / 96, scale);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(20, -1)]
    [InlineData(float.NaN, 20)]
    public void Invalid_dimensions_are_rejected(float width, float height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiquidGlassOptics.TextureScale(width, height, 96));
        Assert.Throws<ArgumentOutOfRangeException>(() => LiquidGlassOptics.Sample(0, 0, width, height, new GlassCorners(10)));
    }
}
