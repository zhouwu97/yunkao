using Microsoft.UI.Xaml.Controls;

namespace YunKao.Controls;

public sealed partial class ProgressCard : UserControl
{
    public ProgressCard()
    {
        InitializeComponent();
    }

    public void SetProgress(int current, int total, string message, int? saved = null, int? aiPending = null, string? speed = null)
    {
        Progress.Value = total > 0 ? Math.Clamp(current * 100d / total, 0, 100) : 0;
        CurrentText.Text = current.ToString();
        TotalText.Text = total.ToString();
        SavedMetricText.Text = (saved ?? current).ToString();
        AiMetricText.Text = (aiPending ?? 0).ToString();
        SpeedMetricText.Text = string.IsNullOrWhiteSpace(speed) ? "—" : speed;
        ProgressText.Text = string.IsNullOrWhiteSpace(message) ? $"{current} / {total}" : message;
    }
}
