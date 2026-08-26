using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class WorkspaceLayoutTests
{
    [Theory]
    [InlineData(0, WorkspaceLayoutMode.Narrow)]
    // 1024, 1280, 1366, 1440 窗口扣除导航栏和内边距后，ContentGrid 约在 900~1350 之间，必须为 Narrow (Overlay)
    [InlineData(900, WorkspaceLayoutMode.Narrow)]
    [InlineData(1170, WorkspaceLayoutMode.Narrow)]
    [InlineData(1250, WorkspaceLayoutMode.Narrow)]
    [InlineData(1340, WorkspaceLayoutMode.Narrow)]
    [InlineData(1395.99, WorkspaceLayoutMode.Narrow)]
    // >= 1396 (对应约 1520~1560px 窗口) 才允许 Dock (Wide)
    [InlineData(1396, WorkspaceLayoutMode.Wide)]
    [InlineData(1500, WorkspaceLayoutMode.Wide)]
    [InlineData(1679.99, WorkspaceLayoutMode.Wide)]
    [InlineData(1680, WorkspaceLayoutMode.ExtraWide)]
    [InlineData(1920, WorkspaceLayoutMode.ExtraWide)]
    public void Chooses_sidebar_only_when_the_projected_browser_width_is_usable(
        double width,
        WorkspaceLayoutMode expected)
    {
        Assert.Equal(expected, WorkspaceLayoutBreakpoints.GetMode(width));
    }

    [Theory]
    [InlineData(1200, WorkspaceLayoutMode.Narrow, 1200)]
    [InlineData(1396, WorkspaceLayoutMode.Wide, 1040)]
    [InlineData(1500, WorkspaceLayoutMode.Wide, 1144)]
    [InlineData(1800, WorkspaceLayoutMode.ExtraWide, 1444)]
    public void Computes_the_webview_budget_from_the_selected_mode(
        double width,
        WorkspaceLayoutMode mode,
        double expectedBrowserWidth)
    {
        Assert.Equal(
            expectedBrowserWidth,
            WorkspaceLayoutBreakpoints.GetProjectedBrowserWidth(
                width,
                WorkspaceLayoutBreakpoints.GetControlWidth(mode)));
    }
}
