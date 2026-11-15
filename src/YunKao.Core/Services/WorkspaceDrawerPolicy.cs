namespace YunKao.Core.Services;

/// <summary>
/// 工作台任务抽屉的统一策略。抽屉始终覆盖网页，只根据可用宽度限制自身宽度。
/// </summary>
public static class WorkspaceDrawerPolicy
{
    public const double MinWidth = 280;
    public const double DefaultWidth = 344;
    public const double MaxWidth = 344;
    public const bool AlwaysOverlay = true;

    public static double GetWidth(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return DefaultWidth;
        }

        return Math.Clamp(availableWidth, MinWidth, MaxWidth);
    }
}
