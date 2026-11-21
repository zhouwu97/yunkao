using Microsoft.UI.Xaml.Controls;

namespace YunKao.Services;

public sealed record WorkspaceBannerInfo(
    string Title,
    string Message,
    InfoBarSeverity Severity,
    string? ActionLabel,
    Action? ActionCallback,
    bool ReturnToWorkspace = true);

/// <summary>
/// 应用级横幅通知。业务层只发出通知，由窗口外壳统一用 InfoBar 显示。
/// </summary>
public sealed class NotificationService
{
    public event EventHandler<WorkspaceBannerInfo>? Requested;

    public void Show(WorkspaceBannerInfo info) => Requested?.Invoke(this, info);

    public void Show(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        => Show(new WorkspaceBannerInfo(title, message, severity, null, null));
}
