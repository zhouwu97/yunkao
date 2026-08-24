using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YunKao.Controls;

public sealed partial class ExportCard : UserControl
{
    public ExportCard()
    {
        InitializeComponent();
    }

    public event EventHandler<string>? ExportRequested;

    private void OnPdfClick(object sender, RoutedEventArgs args) => ExportRequested?.Invoke(this, "pdf");
    private void OnDocxClick(object sender, RoutedEventArgs args) => ExportRequested?.Invoke(this, "docx");
    private void OnMoreClick(object sender, RoutedEventArgs args) => ExportRequested?.Invoke(this, "more");
}
