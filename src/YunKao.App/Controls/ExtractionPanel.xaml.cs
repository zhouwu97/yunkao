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
    public event EventHandler<string>? ExportRequested;

    public void SetState(string state, bool running, bool paused)
    {
        StateText.Text = state;
        StartButton.IsEnabled = !running;
        PauseButton.IsEnabled = running || paused;
        PauseButton.Content = paused ? "继续" : "暂停";
        StopButton.IsEnabled = running || paused;
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => StartRequested?.Invoke(this, EventArgs.Empty);
    private void OnPauseClick(object sender, RoutedEventArgs e) => PauseRequested?.Invoke(this, EventArgs.Empty);
    private void OnStopClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);
    private void OnPdfClick(object sender, RoutedEventArgs e) => ExportRequested?.Invoke(this, "pdf");
    private void OnDocxClick(object sender, RoutedEventArgs e) => ExportRequested?.Invoke(this, "docx");
    private void OnMoreExportClick(object sender, RoutedEventArgs e) => ExportRequested?.Invoke(this, "more");
}
