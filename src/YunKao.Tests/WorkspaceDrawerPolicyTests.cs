using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class WorkspaceDrawerPolicyTests
{
    [Fact]
    public void The_task_panel_is_always_an_overlay()
    {
        Assert.True(WorkspaceDrawerPolicy.AlwaysOverlay);
    }

    [Theory]
    [InlineData(200, 280)]
    [InlineData(328, 328)]
    [InlineData(392, 344)]
    [InlineData(900, 344)]
    public void Keeps_the_drawer_width_inside_its_readable_bounds(
        double pageWidth,
        double expectedWidth)
    {
        Assert.Equal(expectedWidth, WorkspaceDrawerPolicy.GetWidth(pageWidth));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void Uses_the_default_width_for_invalid_page_measurements(double pageWidth)
    {
        Assert.Equal(WorkspaceDrawerPolicy.DefaultWidth, WorkspaceDrawerPolicy.GetWidth(pageWidth));
    }
}
