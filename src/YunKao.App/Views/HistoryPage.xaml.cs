using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YunKao.Core.Models;

namespace YunKao.Views;

public sealed partial class HistoryPage : Page
{
    private DateTimeOffset? _cursor;
    private bool _loading;
    public HistoryPage()
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
            if (reset) { _cursor = null; SessionsList.Items.Clear(); }
            IReadOnlyList<ExtractionSessionRecord> rows = await App.Services.History.GetSessionsAsync(_cursor, 50);
            foreach (ExtractionSessionRecord row in rows)
            {
                SessionsList.Items.Add(new ListViewItem
                {
                    Content = new StackPanel
                    {
                        Spacing = 3,
                        Children =
                        {
                            new TextBlock { Text = $"{row.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {row.Status}", Style = (Style)Application.Current.Resources["CardTitleStyle"] },
                            new TextBlock { Text = $"{row.QuestionCount} 题 · 重复 {row.DuplicateCount} · 异常 {row.ErrorCount} · AI {row.AiCount} 题", Style = (Style)Application.Current.Resources["SecondaryTextStyle"] },
                            new TextBlock { Text = string.IsNullOrWhiteSpace(row.SourceUrl) ? row.Course : row.SourceUrl, TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["CaptionTextStyle"] },
                        },
                    },
                });
            }

            if (rows.Count > 0) _cursor = rows[^1].StartedAt;
            EmptyText.Visibility = SessionsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            MoreButton.IsEnabled = rows.Count == 50;
        }
        catch (Exception exception)
        {
            EmptyText.Text = "历史记录读取失败：" + exception.Message;
            EmptyText.Visibility = Visibility.Visible;
            App.Services.Diagnostics.Error("历史记录读取失败", exception);
        }
        finally { _loading = false; }
    }
}
