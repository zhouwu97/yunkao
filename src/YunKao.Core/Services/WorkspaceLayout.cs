namespace YunKao.Core.Services;

public enum WorkspaceLayoutMode
{
    Narrow,
    Medium,
    Wide,
}

/// <summary>
/// 工作台布局断点的唯一规则，避免 XAML 页面和验收测试各自维护边界。
/// </summary>
public static class WorkspaceLayoutBreakpoints
{
    public const double MediumMinimumWidth = 900;
    public const double WideMinimumWidth = 1180;

    public static WorkspaceLayoutMode GetMode(double width)
    {
        if (width >= WideMinimumWidth) return WorkspaceLayoutMode.Wide;
        if (width >= MediumMinimumWidth) return WorkspaceLayoutMode.Medium;
        return WorkspaceLayoutMode.Narrow;
    }
}
