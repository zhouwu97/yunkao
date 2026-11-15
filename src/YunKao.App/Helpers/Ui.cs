using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using YunKao.Core.Presentation;

namespace YunKao.Helpers;

/// <summary>
/// x:Bind 使用的纯展示函数。只返回与主题无关的资源（样式、状态色），
/// 随主题变化的画刷一律在 XAML 中用 {ThemeResource} 引用。
/// </summary>
public static class Ui
{
    public static Style ToneStyle(TaskActionTone tone) => (Style)Application.Current.Resources[tone switch
    {
        TaskActionTone.Accent => "PrimaryButtonStyle",
        TaskActionTone.Danger => "DangerButtonStyle",
        _ => "SecondaryButtonStyle",
    }];

    public static Brush StatusBrush(TaskStatusKind kind) => (Brush)Application.Current.Resources[kind switch
    {
        TaskStatusKind.Running => "StatusRunningBrush",
        TaskStatusKind.Paused => "StatusPausedBrush",
        TaskStatusKind.Completing => "StatusAiBrush",
        TaskStatusKind.Completed => "StatusSuccessBrush",
        TaskStatusKind.Error => "StatusErrorBrush",
        TaskStatusKind.Recovering => "StatusPausedBrush",
        TaskStatusKind.Waiting => "StatusIdleBrush",
        _ => "StatusReadyBrush",
    }];

    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility HasText(string? value) => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool value) => !value;
}
