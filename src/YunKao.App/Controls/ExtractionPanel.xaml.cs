using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YunKao.Core.Services;

namespace YunKao.Controls;

public sealed partial class ExtractionPanel : UserControl
{
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

    /// <summary>
    /// 按唯一任务状态派生所有按钮，控件本身不维护第二套运行标记。
    /// </summary>
    public void SetState(
        ExtractionStatus status,
        int savedCount,
        bool isPracticeReady,
        bool isBrowserRecovering,
        bool isLoginPage)
    {
        bool running = status == ExtractionStatus.Running;
        bool paused = status == ExtractionStatus.Paused;
        bool active = status is ExtractionStatus.Running or ExtractionStatus.Paused or ExtractionStatus.Completing;
        bool canStart = ExtractionControlPolicy.CanStart(status, isPracticeReady, isBrowserRecovering);
        StateText.Text = isBrowserRecovering
            ? "页面恢复中"
            : status switch
            {
                ExtractionStatus.Running => "提取中",
                ExtractionStatus.Paused => "已暂停",
                ExtractionStatus.Completing => "等待 AI 完成",
                ExtractionStatus.Completed => "已完成 · 可导出",
                ExtractionStatus.Error => "异常 · 可重新开始",
                _ when savedCount > 0 => "已停止 · 可导出",
                _ when isLoginPage => "待机 · 请先登录",
                _ => isPracticeReady ? "就绪 · 点击开始提取" : "待机 · 请先进入练习页面",
            };
        HintText.Text = isBrowserRecovering
            ? "正在重建页面组件，恢复完成前不能继续提取"
            : status switch
            {
                ExtractionStatus.Running => "正在等待下一题内容稳定…",
                ExtractionStatus.Paused => "已暂停，不会自动推进下一题",
                ExtractionStatus.Completing => "题目已完成，正在等待 AI 补全",
                _ when isLoginPage => "登录完成并进入练习后可开始提取",
                _ => isPracticeReady ? "已检测到练习题目，点击开始提取" : "进入练习后可自动连续提取",
            };
        StartButton.IsEnabled = canStart;
        StartButton.Label = isBrowserRecovering
            ? "正在恢复…"
            : !active && isLoginPage ? "请先登录"
            : !active && !isPracticeReady ? "请先进入练习"
            : "开始提取";
        PauseButton.IsEnabled = running
            || ExtractionControlPolicy.CanResume(status, isPracticeReady, isBrowserRecovering);
        PauseButton.Label = isBrowserRecovering ? "正在恢复…" : paused ? "继续提取" : "暂停提取";
        StopButton.IsEnabled = running || paused;
        MoreButton.IsEnabled = active || savedCount > 0;
    }

    public void SetInterrupted(int questionCount)
    {
        RestoreButton.Label = $"恢复上次 · {questionCount} 题";
        RestoreButton.Visibility = Visibility.Visible;
    }

    public void ClearInterrupted() => RestoreButton.Visibility = Visibility.Collapsed;

    private void OnStartClick(object sender, RoutedEventArgs e) => StartRequested?.Invoke(this, EventArgs.Empty);
    private void OnPauseClick(object sender, RoutedEventArgs e) => PauseRequested?.Invoke(this, EventArgs.Empty);
    private void OnStopClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);
    private void OnClearClick(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);
    private void OnRestartClick(object sender, RoutedEventArgs e) => RestartRequested?.Invoke(this, EventArgs.Empty);
    private void OnRestoreClick(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(this, EventArgs.Empty);
}
