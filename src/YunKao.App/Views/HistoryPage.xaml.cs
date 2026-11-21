using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using YunKao.Core.Models;
using YunKao.Core.Services;
using YunKao.ViewModels;

namespace YunKao.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync(reset: true);
    }

    public HistoryViewModel ViewModel { get; } = new();

    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await ViewModel.LoadAsync(reset: true);
    private async void OnMoreClick(object sender, RoutedEventArgs args) => await ViewModel.LoadAsync(reset: false);
    private void OnGoToWorkspaceClick(object sender, RoutedEventArgs args) => App.MainWindow?.ShowPage(YunKao.Controls.NavigationTarget.Workspace);

    private static SessionRow? RowOf(object sender) => (sender as FrameworkElement)?.Tag as SessionRow;

    private async void OnRestoreClick(object sender, RoutedEventArgs args)
    {
        if (RowOf(sender) is not { } row) return;
        try
        {
            ExtractionSessionSnapshot? snapshot = await App.Services.History.GetSessionSnapshotAsync(row.Record.SessionId);
            if (snapshot is null || snapshot.Questions.Count == 0)
            {
                ViewModel.StatusText = "未能读取到该任务保存的题目明细。";
                return;
            }

            ExtractionSession current = App.Services.Workspace.Session;
            bool currentActive = current.Status is ExtractionStatus.Running or ExtractionStatus.Paused or ExtractionStatus.Completing
                || current.SavedCount > 0;
            if (currentActive)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "恢复历史任务？",
                    Content = $"当前工作台任务已有 {current.SavedCount} 道题目。恢复历史任务将安全停止并保存当前任务，载入此历史快照（{snapshot.Questions.Count} 题）。是否继续？",
                    PrimaryButtonText = "确认恢复",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Primary,
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    ViewModel.StatusText = "已取消恢复历史任务。";
                    return;
                }
            }

            if (App.Services.Coordinator is null)
            {
                ViewModel.StatusText = "工作台尚未就绪，请稍后重试。";
                return;
            }

            bool restored = await App.Services.Coordinator.RestoreHistoricalSessionAsync(snapshot, force: true);
            ViewModel.StatusText = restored
                ? $"已恢复任务（{snapshot.Questions.Count} 题），处于暂停状态，可回到工作台确认页面后继续。"
                : "恢复失败，请重试。";
            if (restored) App.MainWindow?.ShowPage(YunKao.Controls.NavigationTarget.Workspace);
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = "恢复任务失败：" + exception.Message;
        }
    }

    private void OnCopyUrlClick(object sender, RoutedEventArgs args)
    {
        if (RowOf(sender) is not { HasUrl: true } row) return;
        var package = new DataPackage();
        package.SetText(row.Url);
        Clipboard.SetContent(package);
        ViewModel.StatusText = "已复制题库链接到剪贴板";
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        if (RowOf(sender) is not { } row) return;
        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除历史记录？",
            Content = $"将删除「{row.Title}」的本地题目快照，删除后无法恢复此任务。已导出的文件不受影响。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await App.Services.History.DeleteSessionAsync(row.Record.SessionId);
            await ViewModel.LoadAsync(reset: true);
            ViewModel.StatusText = "已删除历史记录";
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = "删除失败：" + exception.Message;
        }
    }
}
