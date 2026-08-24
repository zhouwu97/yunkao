using Microsoft.UI.Xaml.Controls;

namespace YunKao.Controls;

public sealed partial class ProgressCard : UserControl
{
    public ProgressCard()
    {
        InitializeComponent();
    }

    public void SetProgress(int current, int total, string message)
    {
        Progress.Value = total > 0 ? Math.Clamp(current * 100d / total, 0, 100) : 0;
        ProgressText.Text = string.IsNullOrWhiteSpace(message) ? $"{current} / {total}" : message;
    }
}
