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
    [InlineData(1380, WorkspaceLayoutMode.Narrow)]
    [InlineData(1411.99, WorkspaceLayoutMode.Narrow)]
    // >= 1412（进入迟滞门槛）才从 Narrow 进入 Dock (Wide)
    [InlineData(1412, WorkspaceLayoutMode.Wide)]
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
    // 处于 Narrow 模式时，在 1380~1411 维持 Narrow 避免跳变
    [InlineData(1396, WorkspaceLayoutMode.Narrow, WorkspaceLayoutMode.Narrow)]
    [InlineData(1411.99, WorkspaceLayoutMode.Narrow, WorkspaceLayoutMode.Narrow)]
    [InlineData(1412, WorkspaceLayoutMode.Narrow, WorkspaceLayoutMode.Wide)]
    // 处于 Wide 模式时，在 1380~1412 维持 Wide 避免拖拽抖动，直到 < 1380 才回退 Narrow
    [InlineData(1400, WorkspaceLayoutMode.Wide, WorkspaceLayoutMode.Wide)]
    [InlineData(1380, WorkspaceLayoutMode.Wide, WorkspaceLayoutMode.Wide)]
    [InlineData(1379.99, WorkspaceLayoutMode.Wide, WorkspaceLayoutMode.Narrow)]
    public void Hysteresis_prevents_frequent_layout_toggling_around_threshold(
        double width,
        WorkspaceLayoutMode currentMode,
        WorkspaceLayoutMode expectedMode)
    {
        Assert.Equal(expectedMode, WorkspaceLayoutBreakpoints.GetMode(width, currentMode));
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
