using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class BrowserZoomCalculatorTests
{
    [Fact]
    public void Login_fits_the_height_without_changing_the_reported_page_width()
    {
        BrowserZoomResult result = BrowserZoomCalculator.Calculate(1018, 1240, new BrowserPageState(true, false), 552, 742);
        Assert.Equal(74, result.Percent);
        Assert.Equal(1240, result.ScrollWidth);
        Assert.True(742 * result.Percent / 100 <= 552);
    }

    [Fact]
    public void Practice_does_not_shrink_a_long_question_to_fit_the_height()
    {
        BrowserZoomResult result = BrowserZoomCalculator.Calculate(1200, 1200, new BrowserPageState(false, true), 600, 2000);
        Assert.Equal(100, result.Percent);
    }

    [Theory]
    [InlineData(1018, 1240, 82)]
    [InlineData(1000, 1118, 89)]
    [InlineData(1120, 1120, 100)]
    public void Rounded_percentage_keeps_content_inside_the_viewport(double viewportWidth, double pageWidth, int expectedPercent)
    {
        BrowserZoomResult result = BrowserZoomCalculator.Calculate(viewportWidth, pageWidth, new BrowserPageState(true, false));
        Assert.Equal(expectedPercent, result.Percent);
        Assert.True(pageWidth * result.Percent / 100 <= viewportWidth);
    }

    [Theory]
    [InlineData(1200, 1200, false, false, 1.0)]
    [InlineData(900, 1200, false, true, 0.75)]
    [InlineData(900, 1400, true, false, 0.68)]
    public void Calculates_a_page_scale_from_viewport_and_page_width(
        double viewportWidth,
        double scrollWidth,
        bool isLogin,
        bool isPractice,
        double expectedScale)
    {
        BrowserZoomResult result = BrowserZoomCalculator.Calculate(
            viewportWidth,
            scrollWidth,
            new BrowserPageState(isLogin, isPractice));

        Assert.Equal(expectedScale, result.Scale, precision: 3);
        Assert.Equal((int)Math.Round(expectedScale * 100), result.Percent);
    }

    [Theory]
    [InlineData(0, 1200)]
    [InlineData(900, 0)]
    [InlineData(double.NaN, 1200)]
    [InlineData(900, double.PositiveInfinity)]
    public void Uses_100_percent_when_page_dimensions_are_not_measurable(
        double viewportWidth,
        double scrollWidth)
    {
        BrowserZoomResult result = BrowserZoomCalculator.Calculate(
            viewportWidth,
            scrollWidth,
            new BrowserPageState(false, true));

        Assert.Equal(1.0, result.Scale);
        Assert.Equal(100, result.Percent);
        Assert.False(result.IsMeasured);
    }
}
