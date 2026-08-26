using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using YunKao.Core.Models;
using YunKao.Core.Services;
using YunKao.Services;

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
                bool fileExists = File.Exists(row.FilePath);
                string fileName = Path.GetFileName(row.FilePath);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = row.FilePath;

                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                
                var openBtn = CreateActionButton("打开", row, OnOpenClick);
                openBtn.IsEnabled = fileExists;
                actions.Children.Add(openBtn);

                var folderBtn = CreateActionButton("文件夹", row, OnOpenFolderClick);
                actions.Children.Add(folderBtn);

                var moreFlyout = new MenuFlyout();
                var copyPathItem = new MenuFlyoutItem { Text = "复制完整路径", Tag = row };
                copyPathItem.Click += OnCopyPathClick;
                moreFlyout.Items.Add(copyPathItem);

                if (!string.IsNullOrWhiteSpace(row.SessionId))
                {
                    var reExportItem = new MenuFlyoutItem { Text = "重新导出", Tag = row };
                    reExportItem.Click += OnReExportClick;
                    moreFlyout.Items.Add(reExportItem);
                }

                var deleteItem = new MenuFlyoutItem { Text = "删除记录", Tag = row };
                deleteItem.Click += OnDeleteRecordClick;
                moreFlyout.Items.Add(deleteItem);

                var moreBtn = new Button
                {
                    Content = "⋯",
                    Style = (Style)Application.Current.Resources["QuietButtonStyle"],
                    Flyout = moreFlyout,
                };
                actions.Children.Add(moreBtn);

                var grid = new Grid { ColumnSpacing = 10 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                string statusExtra = fileExists ? "" : " · ⚠️ 文件已移动或删除";
                var fileText = new TextBlock
                {
                    Text = fileName,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Style = (Style)Application.Current.Resources["SecondaryTextStyle"]
                };
                ToolTipService.SetToolTip(fileText, row.FilePath);

                var text = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = $"{row.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {row.Format} · {row.QuestionCount} 题 · {row.Status}{statusExtra}", Style = (Style)Application.Current.Resources["CardTitleStyle"] },
                        fileText,
                    },
                };
                Grid.SetColumn(text, 0);
                Grid.SetColumn(actions, 1);
                grid.Children.Add(text);
                grid.Children.Add(actions);
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

    private static Button CreateActionButton(string label, ExportRecord record, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Tag = record,
            Style = (Style)Application.Current.Resources["QuietButtonStyle"],
        };
        button.Click += handler;
        return button;
    }

    private void OnOpenClick(object sender, RoutedEventArgs args)
    {
        if (!TryGetRecord(sender, out ExportRecord record)) return;
        if (!File.Exists(record.FilePath))
        {
            StatusText.Text = "导出文件已不存在：" + record.FilePath;
            return;
        }
        Process.Start(new ProcessStartInfo(record.FilePath) { UseShellExecute = true });
        StatusText.Text = "已打开文件";
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs args)
    {
        if (!TryGetRecord(sender, out ExportRecord record)) return;
        string? directory = Path.GetDirectoryName(record.FilePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            StatusText.Text = "导出目录已不存在：" + (directory ?? record.FilePath);
            return;
        }
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        StatusText.Text = "已打开所在文件夹";
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs args)
    {
        if (!TryGetRecord(sender, out ExportRecord record)) return;
        var data = new DataPackage();
        data.SetText(record.FilePath);
        Clipboard.SetContent(data);
        StatusText.Text = "文件路径已复制";
    }

    private async void OnReExportClick(object sender, RoutedEventArgs args)
    {
        if (!TryGetRecord(sender, out ExportRecord record)) return;
        try
        {
            ExtractionSessionSnapshot? snapshot = await App.Services.History.GetSessionSnapshotAsync(record.SessionId);
            if (snapshot is null || snapshot.Questions.Count == 0)
            {
                StatusText.Text = "找不到可重新导出的题目快照";
                return;
            }

            AppSettings settings = App.Services.Settings.Load();
            string format = NormalizeFormat(record.Format);
            string directory = string.IsNullOrWhiteSpace(settings.DefaultExportDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : settings.DefaultExportDirectory;
            string path = ExportPathGenerator.CreateUniquePath(directory, settings.ExportPrefix, format);
            StatusText.Text = $"正在重新导出 {snapshot.Questions.Count} 题…";
            ExportResult result = await App.Services.Exports.ExportAsync(
                new ExportRequest(
                    format,
                    path,
                    snapshot.Questions.Select(question => question.Clone()).ToArray(),
                    record.IncludeAnswers,
                    Watermark: true));
            await App.Services.History.SaveExportAsync(new ExportRecord(
                0,
                result.Format.ToUpperInvariant(),
                result.FilePath,
                result.QuestionCount,
                DateTimeOffset.Now,
                "completed",
                snapshot.SessionId,
                record.IncludeAnswers));
            if (settings.AutoOpenAfterExport)
            {
                Process.Start(new ProcessStartInfo(result.FilePath) { UseShellExecute = true });
            }
            StatusText.Text = "重新导出完成";
            await LoadAsync(reset: true);
        }
        catch (Exception exception)
        {
            StatusText.Text = "重新导出失败：" + exception.Message;
            App.Services.Diagnostics.Error("历史导出重新生成失败", exception);
        }
    }

    private async void OnDeleteRecordClick(object sender, RoutedEventArgs args)
    {
        if (!TryGetRecord(sender, out ExportRecord record)) return;
        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除导出记录？",
            Content = "仅删除本地记录，不会删除已生成的文件。",
            PrimaryButtonText = "删除记录",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        await App.Services.History.DeleteExportAsync(record.Id);
        StatusText.Text = "导出记录已删除，文件未受影响";
        await LoadAsync(reset: true);
    }

    private static bool TryGetRecord(object sender, out ExportRecord record)
    {
        if (sender is Button { Tag: ExportRecord item })
        {
            record = item;
            return true;
        }
        if (sender is MenuFlyoutItem { Tag: ExportRecord flyoutItem })
        {
            record = flyoutItem;
            return true;
        }
        record = default!;
        return false;
    }

    private static string NormalizeFormat(string format)
    {
        string normalized = (format ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "markdown" or "md" => "md",
            "txt" or "docx" or "pdf" => normalized,
            _ => throw new InvalidOperationException("历史记录包含不支持的导出格式。"),
        };
    }
}
