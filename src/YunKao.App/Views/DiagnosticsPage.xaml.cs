using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using YunKao.Core.Models;
using YunKao.Core.Services;

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

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await RefreshAsync();
    private async void OnMoreClick(object sender, RoutedEventArgs args) => await LoadAsync(reset: false);

    private async void OnCopyClick(object sender, RoutedEventArgs args)
    {
        string report = await BuildReportAsync();
        var data = new DataPackage();
        data.SetText(report);
        Clipboard.SetContent(data);
        StatusText.Text = "诊断信息已复制";
    }

    private async void OnExportLogClick(object sender, RoutedEventArgs args)
    {
        try
        {
            string path = Path.Combine(
                App.Services.Diagnostics.LogDirectory,
                $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(path, await BuildReportAsync(), Encoding.UTF8);
            StatusText.Text = "诊断文件已导出：" + path;
        }
        catch (Exception exception)
        {
            StatusText.Text = "导出诊断失败：" + exception.Message;
            App.Services.Diagnostics.Error("导出诊断失败", exception);
        }
    }

    private void OnOpenLogDirectoryClick(object sender, RoutedEventArgs args)
    {
        string directory = App.Services.Diagnostics.LogDirectory;
        if (!Directory.Exists(directory))
        {
            StatusText.Text = "日志目录不可用";
            return;
        }
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        StatusText.Text = "已打开日志目录";
    }

    private async Task RefreshAsync()
    {
        ReportText.Text = await BuildReportAsync();
        await LoadAsync(reset: true);
    }

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

    private async Task<string> BuildReportAsync()
    {
        AppSettings settings = App.Services.Settings.Load();
        _ = App.Services.Settings.TryGetCredentialStoreStatus(out string credentialStatus);
        ExtractionSession session = App.Services.Workspace.Session;
        IReadOnlyList<ExportRecord> recentExports = await App.Services.History.GetExportsAsync(limit: 1);
        ExportRecord? latestExport = recentExports.FirstOrDefault();
        using Process process = Process.GetCurrentProcess();
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "未知";
        string dpi = XamlRoot is null ? "未知" : $"{XamlRoot.RasterizationScale * 96:0} DPI";
        string latestExportText = latestExport is null
            ? "无"
            : $"{latestExport.Format} · {latestExport.FilePath} · {latestExport.Status}";
        var report = new StringBuilder();
        report.AppendLine($"应用版本：{version}");
        report.AppendLine($"WebView2：{App.Services.Workspace.BrowserVersion}");
        report.AppendLine($"配置路径：{App.Services.Settings.SettingsPath}");
        report.AppendLine($"凭据库：{credentialStatus}");
        report.AppendLine($"当前 URL：{App.Services.Workspace.CurrentUrl?.AbsoluteUri ?? "无"}");
        report.AppendLine($"WebChannel：{(App.Services.Workspace.BridgeInstalled ? "已连接" : "未连接")}");
        report.AppendLine($"浏览器状态：{App.Services.Workspace.BrowserStatus}");
        report.AppendLine($"Worker：{(App.Services.Worker.IsRunning ? "运行中" : "未启动")}");
        report.AppendLine($"本次任务：{session.Status} · {session.SavedCount} 题 · 进度 {session.Current}/{session.Total} · 重复 {session.DuplicateCount} · 异常 {session.ErrorCount} · AI 待处理 {session.AiPending} / 失败 {session.AiFailedCount}");
        report.AppendLine($"任务来源：{session.SourceUrl}");
        report.AppendLine($"最近导出：{latestExportText}");
        report.AppendLine($"日志路径：{App.Services.Diagnostics.LogPath}");
        report.AppendLine($"系统：{Environment.OSVersion.VersionString} · {Environment.Is64BitOperatingSystem switch { true => "x64", false => "x86" }} · {dpi}");
        report.AppendLine($"进程内存：{process.WorkingSet64 / 1024d / 1024d:0.0} MB · CPU 逻辑核心：{Environment.ProcessorCount}");
        report.AppendLine($"导出设置：{settings.DefaultExportFormat} · {(settings.ExportWithoutAnswers ? "练习版" : "答案版")}");
        return report.ToString().TrimEnd();
    }
}
