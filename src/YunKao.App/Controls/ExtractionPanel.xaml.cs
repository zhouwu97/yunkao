using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YunKao.Core.Services;

namespace YunKao.Controls;

public enum PanelPrimaryAction
{
    DisabledWait,
    Start,
    Pause,
    Resume,
    Export,
    Restart,
}

public sealed partial class ExtractionPanel : UserControl
{
    private PanelPrimaryAction _currentPrimaryAction = PanelPrimaryAction.DisabledWait;
    private int _interruptedCount = 0;

    public ExtractionPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? StartRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? ClearRequested;
    public event EventHandler? RestartRequested;
    public event EventHandler? RestoreRequested;
    public event EventHandler? ExportTriggerRequested;

    /// <summary>
    /// 按唯一任务状态派生所有按钮文案、样式与可见性，确保始终只有一个明确的 Primary Action。
    /// </summary>
    public void SetState(
        ExtractionStatus status,
        int savedCount,
        bool isPracticeReady,
        bool isBrowserRecovering,
        bool isLoginPage)
    {
        if (isBrowserRecovering)
        {
            SetStateVisual(
                icon: "\uE895",
                state: "页面恢复中",
                step: "正在恢复",
                hint: "正在重建页面组件，恢复完成前暂不能提取",
                actionLabel: "正在恢复…",
                action: PanelPrimaryAction.DisabledWait,
                primaryVariant: LiquidButtonVariant.Soft,
                showStopButton: false);
            return;
        }

        switch (status)
        {
            case ExtractionStatus.Running:
                SetStateVisual(
                    icon: "\uE768",
                    state: $"提取中 · 已保存 {savedCount} 题",
                    step: "正在连续提取",
                    hint: "保持页面打开即可，提取完成将自动提示",
                    actionLabel: "暂停提取",
                    action: PanelPrimaryAction.Pause,
                    primaryVariant: LiquidButtonVariant.Soft,
                    showStopButton: true);
                break;

            case ExtractionStatus.Paused:
                SetStateVisual(
                    icon: "\uE769",
                    state: $"已暂停 · 已存 {savedCount} 题",
                    step: "任务暂停中",
                    hint: "当前不会自动跳转下一题，点击可随时继续",
                    actionLabel: "继续提取",
                    action: PanelPrimaryAction.Resume,
                    primaryVariant: LiquidButtonVariant.Primary,
                    showStopButton: true);
                break;

            case ExtractionStatus.Completing:
                SetStateVisual(
                    icon: "\uE946",
                    state: "等待 AI 补全",
                    step: "题目已提取完毕",
                    hint: "正在等待后台 AI 补全解析，请稍候…",
                    actionLabel: "AI 处理中…",
                    action: PanelPrimaryAction.DisabledWait,
                    primaryVariant: LiquidButtonVariant.Soft,
                    showStopButton: false);
                break;

            case ExtractionStatus.Completed:
                SetStateVisual(
                    icon: "\uE73E",
                    state: "提取完成 · 题库就绪",
                    step: "下一步：导出题库",
                    hint: $"{savedCount} 道题目已完整保存在本机，可直接导出",
                    actionLabel: "导出题库",
                    action: PanelPrimaryAction.Export,
                    primaryVariant: LiquidButtonVariant.Primary,
                    showStopButton: false);
                break;

            case ExtractionStatus.Error:
                SetStateVisual(
                    icon: "\uE783",
                    state: "提取异常 · 可重试",
                    step: "遇到异常",
                    hint: "提取过程中断，点击重新开始或检查网页状态",
                    actionLabel: "重新开始",
                    action: PanelPrimaryAction.Restart,
                    primaryVariant: LiquidButtonVariant.Coral,
                    showStopButton: false);
                break;

            default: // Idle
                if (savedCount > 0)
                {
                    SetStateVisual(
                        icon: "\uE73E",
                        state: $"已就绪 · 本地 {savedCount} 题",
                        step: isPracticeReady ? "可以继续提取" : "可直接导出",
                        hint: isPracticeReady ? "当前页面为练习页，可继续提取新题" : "可直接在下方导出，或进入练习页继续提取",
                        actionLabel: isPracticeReady ? "开始提取" : "导出题库",
                        action: isPracticeReady ? PanelPrimaryAction.Start : PanelPrimaryAction.Export,
                        primaryVariant: LiquidButtonVariant.Primary,
                        showStopButton: false);
                }
                else if (isLoginPage)
                {
                    SetStateVisual(
                        icon: "\uE77B",
                        state: "待机 · 请先登录",
                        step: "第一步：登录系统",
                        hint: "在左侧网页中完成登录后，进入练习页面即可提取",
                        actionLabel: "请先登录",
                        action: PanelPrimaryAction.DisabledWait,
                        primaryVariant: LiquidButtonVariant.Ghost,
                        showStopButton: false);
                }
                else if (isPracticeReady)
                {
                    SetStateVisual(
                        icon: "\uE768",
                        state: "页面已就绪",
                        step: "下一步：可以开始提取",
                        hint: "当前页面已识别为练习页面，点击开始提取",
                        actionLabel: "开始提取",
                        action: PanelPrimaryAction.Start,
                        primaryVariant: LiquidButtonVariant.Primary,
                        showStopButton: false);
                }
                else
                {
                    SetStateVisual(
                        icon: "\uE8A5",
                        state: "待机 · 等待进入练习页面",
                        step: "下一步：进入练习页",
                        hint: "检测到练习页面后才能开始连续提取",
                        actionLabel: "等待进入练习页",
                        action: PanelPrimaryAction.DisabledWait,
                        primaryVariant: LiquidButtonVariant.Ghost,
                        showStopButton: false);
                }
                break;
        }

        ClearMenuItem.IsEnabled = savedCount > 0;
        RestartMenuItem.IsEnabled = savedCount > 0 || status == ExtractionStatus.Error;
    }

    private void SetStateVisual(
        string icon,
        string state,
        string step,
        string hint,
        string actionLabel,
        PanelPrimaryAction action,
        LiquidButtonVariant primaryVariant,
        bool showStopButton)
    {
        StatusIcon.Glyph = icon;
        StateText.Text = state;
        StepLabel.Text = step;
        HintText.Text = hint;

        _currentPrimaryAction = action;
        PrimaryActionButton.Label = actionLabel;
        PrimaryActionButton.IsEnabled = action != PanelPrimaryAction.DisabledWait;
        PrimaryActionButton.Variant = primaryVariant;

        SecondaryStopButton.Visibility = showStopButton ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetInterrupted(int questionCount)
    {
        _interruptedCount = questionCount;
        RestoreSeparator.Visibility = Visibility.Visible;
        RestoreMenuItem.Visibility = Visibility.Visible;
        RestoreMenuItem.Text = $"恢复上次任务 ({questionCount} 题)";
    }

    public void ClearInterrupted()
    {
        _interruptedCount = 0;
        RestoreSeparator.Visibility = Visibility.Collapsed;
        RestoreMenuItem.Visibility = Visibility.Collapsed;
    }

    private void OnPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        switch (_currentPrimaryAction)
        {
            case PanelPrimaryAction.Start:
                StartRequested?.Invoke(this, EventArgs.Empty);
                break;
            case PanelPrimaryAction.Pause:
            case PanelPrimaryAction.Resume:
                PauseRequested?.Invoke(this, EventArgs.Empty);
                break;
            case PanelPrimaryAction.Export:
                ExportTriggerRequested?.Invoke(this, EventArgs.Empty);
                break;
            case PanelPrimaryAction.Restart:
                RestartRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void OnPauseClick(object sender, RoutedEventArgs e) => PauseRequested?.Invoke(this, EventArgs.Empty);
    private void OnStopClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);
    private void OnClearClick(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);
    private void OnRestartClick(object sender, RoutedEventArgs e) => RestartRequested?.Invoke(this, EventArgs.Empty);
    private void OnRestoreClick(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(this, EventArgs.Empty);
}
