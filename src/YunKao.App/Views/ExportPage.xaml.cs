using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YunKao.Core.Models;

namespace YunKao.Views;

public sealed partial class ExportPage : Page
{
    private DateTimeOffset? _cursor;
    private bool _loading;
    public ExportPage()
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
            if (reset) { _cursor = null; ExportsList.Items.Clear(); }
            IReadOnlyList<ExportRecord> rows = await App.Services.History.GetExportsAsync(_cursor, 50);
            foreach (ExportRecord row in rows)
            {
                var openButton = new Button
                {
                    Content = "打开",
                    Tag = row.FilePath,
                    Style = (Style)Application.Current.Resources["QuietButtonStyle"],
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                openButton.Click += OnOpenClick;
                var grid = new Grid { ColumnSpacing = 10 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = $"{row.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {row.Format} · {row.Status}", Style = (Style)Application.Current.Resources["CardTitleStyle"] },
                        new TextBlock { Text = $"{row.QuestionCount} 题 · {row.FilePath}", TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["SecondaryTextStyle"] },
                    },
                };
                Grid.SetColumn(text, 0);
                Grid.SetColumn(openButton, 1);
                grid.Children.Add(text);
                grid.Children.Add(openButton);
                ExportsList.Items.Add(new ListViewItem { Content = grid });
            }

            if (rows.Count > 0) _cursor = rows[^1].CreatedAt;
            EmptyText.Visibility = ExportsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            MoreButton.IsEnabled = rows.Count == 50;
        }
        catch (Exception exception)
        {
            EmptyText.Text = "导出记录读取失败：" + exception.Message;
            EmptyText.Visibility = Visibility.Visible;
            App.Services.Diagnostics.Error("导出记录读取失败", exception);
        }
        finally { _loading = false; }
    }

    private void OnOpenClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || button.Tag is not string path || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
