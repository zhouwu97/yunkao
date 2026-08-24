using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YunKao.Core.Models;

namespace YunKao.Views;

public sealed partial class DiagnosticsPage : Page
{
    private DateTimeOffset? _cursor;
    private bool _loading;
    public DiagnosticsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args) => await LoadAsync(reset: true);
    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await LoadAsync(reset: true);
    private async void OnMoreClick(object sender, RoutedEventArgs args) => await LoadAsync(reset: false);

    private async Task LoadAsync(bool reset)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (reset) { _cursor = null; DiagnosticsList.Items.Clear(); }
            IReadOnlyList<DiagnosticRecord> rows = await App.Services.History.GetDiagnosticsAsync(_cursor, 200);
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

            if (rows.Count > 0) _cursor = rows[^1].CreatedAt;
            EmptyText.Visibility = DiagnosticsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            MoreButton.IsEnabled = rows.Count == 200;
        }
        catch (Exception exception)
        {
            EmptyText.Text = "诊断记录读取失败：" + exception.Message;
            EmptyText.Visibility = Visibility.Visible;
        }
        finally { _loading = false; }
    }
}
