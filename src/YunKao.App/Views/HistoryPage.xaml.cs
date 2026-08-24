using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YunKao.Core.Models;

namespace YunKao.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
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
            IReadOnlyList<ExtractionSessionRecord> rows = await App.Services.History.GetSessionsAsync();
            SessionsList.Items.Clear();
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
                            new TextBlock { Text = $"{row.QuestionCount} 题 · AI {row.AiCount} 题 · {row.Course}", Style = (Style)Application.Current.Resources["SecondaryTextStyle"] },
                        },
                    },
                });
            }

            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            EmptyText.Text = "历史记录读取失败：" + exception.Message;
            EmptyText.Visibility = Visibility.Visible;
            App.Services.Diagnostics.Error("历史记录读取失败", exception);
        }
    }
}
