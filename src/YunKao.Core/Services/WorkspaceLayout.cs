namespace YunKao.Core.Services;

public enum WorkspaceLayoutMode
{
    Narrow,
    Wide,
    ExtraWide,
}

/// <summary>
/// 工作台布局规则。断点由 WebView 的最低舒适宽度（>=1040px）决定，避免挤压 PC 网页。
/// 只有当可用空间同时满足「浏览器最低舒适宽度 (1040px) + 任务栏 (344px) + 间距 (12px) = 1396px」时才允许 Dock 右栏。
/// 其余尺寸（< 1396px，对应整窗约 < 1520~1560px）一律使用 Overlay Drawer，Browser 保持全宽且尺寸恒定。
/// </summary>
public static class WorkspaceLayoutBreakpoints
{
    public const double BrowserColumnSpacing = 12;
    public const double MinComfortableBrowserWidth = 1040;
    public const double DockedControlWidth = 344;
    public const double ExtraWideControlWidth = 344;

    // 门槛：1040 + 344 + 12 = 1396
    public const double MinDockedWorkspaceWidth = MinComfortableBrowserWidth + DockedControlWidth + BrowserColumnSpacing;
    public const double ExtraWideWorkspaceWidth = 1680;

    /// <summary>
    /// 根据工作区内容区宽度选择布局。
    /// </summary>
    public static WorkspaceLayoutMode GetMode(double availableWorkspaceWidth)
    {
        if (!double.IsFinite(availableWorkspaceWidth) || availableWorkspaceWidth < MinDockedWorkspaceWidth)
        {
            return WorkspaceLayoutMode.Narrow;
        }

        if (availableWorkspaceWidth >= ExtraWideWorkspaceWidth)
        {
            return WorkspaceLayoutMode.ExtraWide;
        }

        return WorkspaceLayoutMode.Wide;
    }

    public static double GetControlWidth(WorkspaceLayoutMode mode) => mode switch
    {
        WorkspaceLayoutMode.Wide => DockedControlWidth,
        WorkspaceLayoutMode.ExtraWide => ExtraWideControlWidth,
        _ => 0,
    };

    public static double GetProjectedBrowserWidth(double availableWorkspaceWidth, double controlWidth)
    {
        if (!double.IsFinite(availableWorkspaceWidth) || availableWorkspaceWidth <= 0)
        {
            return 0;
        }

        if (controlWidth <= 0)
        {
            return availableWorkspaceWidth;
        }

        return Math.Max(0, availableWorkspaceWidth - controlWidth - BrowserColumnSpacing);
    }
}
