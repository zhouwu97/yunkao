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
/// 加入迟滞区间（Hysteresis: 1380px ~ 1412px），避免拖动窗口在临界点发生频繁抖动。
/// </summary>
public static class WorkspaceLayoutBreakpoints
{
    public const double BrowserColumnSpacing = 12;
    public const double MinComfortableBrowserWidth = 1040;
    public const double DockedControlWidth = 344;
    public const double ExtraWideControlWidth = 344;

    // 基准门槛：1040 + 344 + 12 = 1396
    public const double MinDockedWorkspaceWidth = MinComfortableBrowserWidth + DockedControlWidth + BrowserColumnSpacing;

    // 迟滞阈值：进入 Dock 需要 >= 1412，退出 Dock 需要 < 1380
    public const double EnterDockThreshold = MinDockedWorkspaceWidth + 16; // 1412
    public const double ExitDockThreshold = MinDockedWorkspaceWidth - 16;  // 1380
    public const double ExtraWideWorkspaceWidth = 1680;

    /// <summary>
    /// 根据当前布局状态与可用宽度计算目标布局模式（带迟滞保护）。
    /// </summary>
    public static WorkspaceLayoutMode GetMode(double availableWorkspaceWidth, WorkspaceLayoutMode currentMode = WorkspaceLayoutMode.Narrow)
    {
        if (!double.IsFinite(availableWorkspaceWidth) || availableWorkspaceWidth <= 0)
        {
            return WorkspaceLayoutMode.Narrow;
        }

        if (currentMode == WorkspaceLayoutMode.Narrow)
        {
            if (availableWorkspaceWidth < EnterDockThreshold)
            {
                return WorkspaceLayoutMode.Narrow;
            }

            return availableWorkspaceWidth >= ExtraWideWorkspaceWidth
                ? WorkspaceLayoutMode.ExtraWide
                : WorkspaceLayoutMode.Wide;
        }

        // 当前处于 Dock 模式 (Wide / ExtraWide)，退出门槛为 ExitDockThreshold (1380)
        if (availableWorkspaceWidth < ExitDockThreshold)
        {
            return WorkspaceLayoutMode.Narrow;
        }

        return availableWorkspaceWidth >= ExtraWideWorkspaceWidth
            ? WorkspaceLayoutMode.ExtraWide
            : WorkspaceLayoutMode.Wide;
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
