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
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                if (row.QuestionCount > 0)
                {
                    var restoreButton = new Button
                    {
                        Content = "恢复为当前任务",
                        Tag = row,
                        Style = (Style)Application.Current.Resources["QuietButtonStyle"],
                    };
                    restoreButton.Click += OnRestoreSessionClick;
                    actions.Children.Add(restoreButton);
                }

                if (!string.IsNullOrWhiteSpace(row.SourceUrl))
                {
                    var copyUrlButton = new Button
                    {
                        Content = "复制链接",
                        Tag = row.SourceUrl,
                        Style = (Style)Application.Current.Resources["QuietButtonStyle"],
                    };
                    copyUrlButton.Click += OnCopyUrlClick;
                    actions.Children.Add(copyUrlButton);
                }

                var deleteButton = new Button
                {
                    Content = "删除记录",
                    Tag = row.SessionId,
                    Style = (Style)Application.Current.Resources["QuietButtonStyle"],
                };
                deleteButton.Click += OnDeleteSessionClick;
                actions.Children.Add(deleteButton);

                var grid = new Grid { ColumnSpacing = 12 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = $"{row.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {(string.IsNullOrWhiteSpace(row.Course) ? "练习题库" : row.Course)} · {row.Status}", Style = (Style)Application.Current.Resources["CardTitleStyle"] },
                        new TextBlock { Text = $"{row.QuestionCount} 题 · 重复 {row.DuplicateCount} · 异常 {row.ErrorCount} · AI {row.AiCount} 题", Style = (Style)Application.Current.Resources["SecondaryTextStyle"] },
                        new TextBlock { Text = string.IsNullOrWhiteSpace(row.SourceUrl) ? "" : row.SourceUrl, TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["CaptionTextStyle"] },
                    },
                };

                Grid.SetColumn(text, 0);
                Grid.SetColumn(actions, 1);
                grid.Children.Add(text);
                grid.Children.Add(actions);
                SessionsList.Items.Add(new ListViewItem { Content = grid });
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

    private async void OnRestoreSessionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ExtractionSessionRecord record }) return;
        try
        {
            ExtractionSessionSnapshot? snapshot = await App.Services.History.GetSessionSnapshotAsync(record.SessionId);
            if (snapshot is null || snapshot.Questions.Count == 0)
            {
                StatusText.Text = "未能读取到该任务保存的题目明细。";
                return;
            }

            bool restored = App.Services.Workspace.Session.Restore(snapshot);
            if (restored)
            {
                StatusText.Text = $"已恢复任务 ({snapshot.Questions.Count} 题) 至工作台，可切回工作台查看或导出。";
            }
            else
            {
                StatusText.Text = "恢复失败，当前工作台可能已有正在运行的任务。";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "恢复任务失败：" + ex.Message;
        }
    }

    private void OnCopyUrlClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(url);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        StatusText.Text = "已复制题库链接到剪贴板";
    }

    private async void OnDeleteSessionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sessionId } || string.IsNullOrWhiteSpace(sessionId)) return;
        try
        {
            await App.Services.History.DeleteSessionAsync(sessionId);
            StatusText.Text = "已删除历史记录";
            await LoadAsync(reset: true);
        }
        catch (Exception ex)
        {
            StatusText.Text = "删除失败：" + ex.Message;
        }
    }
}
