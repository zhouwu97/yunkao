namespace YunKao.Core.Services;

/// <summary>
/// 工作台按钮的状态门禁。UI 只能呈现这个结果，不能在点击后才补做页面检查。
/// </summary>
public static class ExtractionControlPolicy
{
    public static bool CanStart(
        ExtractionStatus status,
        bool isPracticeReady,
        bool isBrowserRecovering)
    {
        bool inactive = status is not (ExtractionStatus.Running or ExtractionStatus.Paused or ExtractionStatus.Completing);
        return inactive && isPracticeReady && !isBrowserRecovering;
    }

    public static bool CanResume(
        ExtractionStatus status,
        bool isPracticeReady,
        bool isBrowserRecovering)
    {
        return status == ExtractionStatus.Paused && isPracticeReady && !isBrowserRecovering;
    }
}
