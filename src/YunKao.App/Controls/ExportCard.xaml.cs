using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YunKao.Controls;

public sealed partial class ExportCard : UserControl
{
    private bool _updatingPracticeMode;
    private int _savedCount;
    private bool _isExporting;

    public ExportCard()
    {
        InitializeComponent();
    }

    public event EventHandler<string>? ExportRequested;
    public event EventHandler<bool>? PracticeModeChanged;

    public void SetSavedCount(int savedCount)
    {
        _savedCount = savedCount;
        UpdateVisibility();
    }

    public void SetExporting(bool exporting)
    {
        _isExporting = exporting;
        PdfButton.IsEnabled = !exporting;
        DocxButton.IsEnabled = !exporting;
        MoreButton.IsEnabled = !exporting;
        PracticeModeToggle.IsEnabled = !exporting;
        StateText.Text = exporting ? "正在导出…" : (_savedCount > 0 ? "就绪" : "待机");
    }

    public void SetExportProgress(int current, int total, string message)
    {
        StateText.Text = total > 0 ? $"{current}/{total}" : message;
    }

    public void SetPracticeMode(bool enabled)
    {
        _updatingPracticeMode = true;
        PracticeModeToggle.IsOn = enabled;
        _updatingPracticeMode = false;
    }

    public void TriggerDefaultExport()
    {
        if (_savedCount > 0 && !_isExporting)
        {
            ExportRequested?.Invoke(this, "pdf");
        }
    }

    private void UpdateVisibility()
    {
        bool hasQuestions = _savedCount > 0;
        EmptyPrompt.Visibility = hasQuestions ? Visibility.Collapsed : Visibility.Visible;
        ExportControlsPanel.Visibility = hasQuestions ? Visibility.Visible : Visibility.Collapsed;
        StateText.Text = _isExporting ? "正在导出…" : (hasQuestions ? "就绪" : "待机");
    }

    private void OnPdfClick(object sender, RoutedEventArgs args) => ExportRequested?.Invoke(this, "pdf");
    private void OnDocxClick(object sender, RoutedEventArgs args) => ExportRequested?.Invoke(this, "docx");
    private void OnMoreFormatClick(object sender, RoutedEventArgs args)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string format)
        {
            ExportRequested?.Invoke(this, format);
        }
    }

    private void OnPracticeModeToggled(object sender, RoutedEventArgs args)
    {
        if (!_updatingPracticeMode) PracticeModeChanged?.Invoke(this, PracticeModeToggle.IsOn);
    }
}
