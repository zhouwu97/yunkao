using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
    public event EventHandler? RestoreRequested;

    public void SetState(string state, bool running, bool paused)
    {
        StateText.Text = state;
        HintText.Text = running || paused
            ? "正在等待下一题内容稳定…"
            : "进入练习后可自动连续提取";
        StartButton.IsEnabled = !running;
        PauseButton.IsEnabled = running || paused;
        PauseButton.Label = paused ? "继续提取" : "暂停提取";
        StopButton.IsEnabled = running || paused;
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
    private void OnRestoreClick(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke(this, EventArgs.Empty);
}
