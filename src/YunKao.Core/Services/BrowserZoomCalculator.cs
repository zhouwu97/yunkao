namespace YunKao.Core.Services;

public enum BrowserZoomPageKind
{
    General,
    LoginOrLanding,
    Practice,
}

public readonly record struct BrowserZoomResult(
    double Scale,
    int Percent,
    double MinimumScale,
    double ScrollWidth,
    double ViewportWidth,
    bool IsMeasured,
    BrowserZoomPageKind PageKind);

/// <summary>
/// 计算网页适应宽度的纯逻辑入口。练习页保留可读性，登录和首页优先保证固定布局完整显示。
/// </summary>
public static class BrowserZoomCalculator
{
    public const double PracticeMinimumScale = 0.75;
    public const double GeneralMinimumScale = 0.70;
    public const double LoginMinimumScale = 0.68;

    public static BrowserZoomResult Calculate(
        double viewportWidth,
        double scrollWidth,
        BrowserPageState pageState,
        double viewportHeight = 0,
        double contentHeight = 0)
    {
        BrowserZoomPageKind pageKind = GetPageKind(pageState);
        double minimumScale = GetMinimumScale(pageKind);
        if (!double.IsFinite(viewportWidth)
            || !double.IsFinite(scrollWidth)
            || viewportWidth <= 0
            || scrollWidth <= 0)
        {
            return new(
                1.0,
                100,
                minimumScale,
                scrollWidth,
                viewportWidth,
                false,
                pageKind);
        }

        double fitScale = viewportWidth / scrollWidth;
        if (pageState.IsLogin && double.IsFinite(viewportHeight) && double.IsFinite(contentHeight)
            && viewportHeight > 0 && contentHeight > 0)
        {
            fitScale = Math.Min(fitScale, viewportHeight / contentHeight);
        }
        double scale = Math.Clamp(fitScale, minimumScale, 1.0);
        // 向下取整，避免百分比四舍五入后重新产生横向溢出。
        int percent = Math.Clamp((int)Math.Floor(scale * 100 + 1e-8), 1, 100);
        return new(scale, percent, minimumScale, scrollWidth, viewportWidth, true, pageKind);
    }

    public static double GetMinimumScale(BrowserPageState pageState)
    {
        return GetMinimumScale(GetPageKind(pageState));
    }

    private static BrowserZoomPageKind GetPageKind(BrowserPageState pageState)
    {
        if (pageState.IsPracticeReady) return BrowserZoomPageKind.Practice;
        return pageState.IsLogin ? BrowserZoomPageKind.LoginOrLanding : BrowserZoomPageKind.General;
    }

    private static double GetMinimumScale(BrowserZoomPageKind pageKind)
    {
        return pageKind switch
        {
            BrowserZoomPageKind.Practice => PracticeMinimumScale,
            BrowserZoomPageKind.LoginOrLanding => LoginMinimumScale,
            _ => GeneralMinimumScale,
        };
    }
}
