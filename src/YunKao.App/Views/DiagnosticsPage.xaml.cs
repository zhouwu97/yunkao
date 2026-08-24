using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YunKao.Core.Models;

namespace YunKao.Views;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args) => await LoadAsync();
    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            IReadOnlyList<DiagnosticRecord> rows = await App.Services.History.GetDiagnosticsAsync();
            DiagnosticsList.Items.Clear();
            foreach (DiagnosticRecord row in rows)
            {
                string line = $"{row.CreatedAt.LocalDateTime:HH:mm:ss} · {row.Level.ToUpperInvariant()} · {row.Message}";
                DiagnosticsList.Items.Add(new ListViewItem
                {
                    Content = new TextBlock
                    {
                        Text = line,
                        TextWrapping = TextWrapping.Wrap,
                        Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
                    },
                });
            }

            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            EmptyText.Text = "诊断记录读取失败：" + exception.Message;
            EmptyText.Visibility = Visibility.Visible;
        }
    }
}
