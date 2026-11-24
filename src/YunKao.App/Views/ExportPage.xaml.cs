using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using YunKao.Core.Models;
using YunKao.Core.Services;
using YunKao.Services;
using YunKao.ViewModels;

namespace YunKao.Views;

public sealed partial class ExportPage : Page
{
    private bool _reExporting;
    public ExportPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync(reset: true);
    }

    public ExportsViewModel ViewModel { get; } = new();

    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await ViewModel.LoadAsync(reset: true);
    private async void OnMoreClick(object sender, RoutedEventArgs args) => await ViewModel.LoadAsync(reset: false);
    private async void OnSearchChanged(object sender, TextChangedEventArgs args)
        => await ViewModel.SearchAsync(((TextBox)sender).Text);

    private void OnFindInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.SelectAll();
        args.Handled = true;
    }

    private async void OnEmptyActionClick(object sender, RoutedEventArgs args)
    {
        if (ViewModel.ReadFailed) await ViewModel.LoadAsync(reset: true);
        else if (!string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            SearchBox.Text = "";
            SearchBox.Focus(FocusState.Keyboard);
        }
        else App.MainWindow?.ShowPage(YunKao.Controls.NavigationTarget.Workspace);
    }
    private Task LoadAsync(bool reset) => ViewModel.LoadAsync(reset);

    private void OnOpenClick(object sender, RoutedEventArgs args)
    {
        if (!TryGetRecord(sender, out ExportRecord record)) return;
        if (!File.Exists(record.FilePath))
        {
            ViewModel.StatusText = "导出文件已不存在：" + record.FilePath;
            return;
        }
        ViewModel.StatusText = FileActions.TryOpen(record.FilePath, out string error)
            ? "已打开文件" : "文件已保存，但无法打开：" + error;
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs args)
    {
        if (!TryGetRecord(sender, out ExportRecord record)) return;
        string? directory = Path.GetDirectoryName(record.FilePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            ViewModel.StatusText = "导出目录已不存在：" + (directory ?? record.FilePath);
            return;
        }
        ViewModel.StatusText = FileActions.TryOpen(directory, out string error)
            ? "已打开所在文件夹" : "无法打开文件夹：" + error;
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs args)
    {
        if (!TryGetRecord(sender, out ExportRecord record)) return;
        var data = new DataPackage();
        data.SetText(record.FilePath);
        Clipboard.SetContent(data);
        ViewModel.StatusText = "文件路径已复制";
    }

    private async void OnReExportClick(object sender, RoutedEventArgs args)
    {
        if (!TryGetRecord(sender, out ExportRecord record)) return;
        if (_reExporting || App.Services.Exports.IsExporting)
        {
            ViewModel.StatusText = "已有文件正在导出，请等待完成。";
            return;
        }
        _reExporting = true;
        try
        {
            ExtractionSessionSnapshot? snapshot = await App.Services.History.GetSessionSnapshotAsync(record.SessionId);
            if (snapshot is null || snapshot.Questions.Count == 0)
            {
                ViewModel.StatusText = "找不到可重新导出的题目快照";
                return;
            }

            AppSettings settings = App.Services.Settings.Load();
            string format = NormalizeFormat(record.Format);
            string directory = string.IsNullOrWhiteSpace(settings.DefaultExportDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : settings.DefaultExportDirectory;
            string path = ExportPathGenerator.CreateUniquePath(directory, settings.ExportPrefix, format);
            ViewModel.StatusText = $"正在重新导出 {snapshot.Questions.Count} 题…";
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
            await LoadAsync(reset: true);
            ViewModel.StatusText = "重新导出完成：" + result.FilePath;
            App.Services.Notifications.Show(new WorkspaceBannerInfo(
                $"重新导出完成 · {result.QuestionCount} 题", result.FilePath,
                InfoBarSeverity.Success, "打开文件夹", () => FileActions.OpenExportFolder(result.FilePath), ReturnToWorkspace: false));
            if (settings.AutoOpenAfterExport && !FileActions.TryOpen(result.FilePath, out string error))
                ViewModel.StatusText = "文件已生成，未能自动打开：" + error + "。路径：" + result.FilePath;
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = "重新导出失败：" + exception.Message;
            App.Services.Diagnostics.Error("历史导出重新生成失败", exception);
        }
        finally
        {
            _reExporting = false;
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
        try
        {
            await App.Services.History.DeleteExportAsync(record.Id);
            await LoadAsync(reset: true);
            ViewModel.StatusText = "导出记录已删除，文件未受影响";
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = "删除记录失败，请重试：" + exception.Message;
        }
    }

    private static bool TryGetRecord(object sender, out ExportRecord record)
    {
        if ((sender as FrameworkElement)?.Tag is ExportRow row)
        {
            record = row.Record;
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
