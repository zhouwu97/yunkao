using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using YunKao.Core.Models;
using YunKao.Core.Services;
using YunKao.Services;
using YunKao.ViewModels;

namespace YunKao.Views;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
        Unloaded += (_, _) => ViewModel.Detach();
    }

    public DiagnosticsViewModel ViewModel { get; } = new();

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool narrow = args.NewSize.Width < 720;
        DiagnosticsLayout.Height = narrow ? 1120 : Math.Max(0, args.NewSize.Height);
        DiagnosticsLayout.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 2, GridUnitType.Star);
        DiagnosticsLayout.ColumnSpacing = narrow ? 0 : 20;
        DiagnosticsLayout.RowDefinitions[0].Height = narrow ? new GridLength(420) : new GridLength(3, GridUnitType.Star);
        DiagnosticsLayout.RowDefinitions[1].Height = narrow ? new GridLength(280) : new GridLength(2, GridUnitType.Star);
        if (narrow && DiagnosticsLayout.RowDefinitions.Count == 2)
            DiagnosticsLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(380) });
        Grid.SetColumn(ActivitySection, narrow ? 0 : 1);
        Grid.SetRow(ActivitySection, narrow ? 2 : 0);
        Grid.SetRowSpan(ActivitySection, narrow ? 1 : 2);
        if (!narrow && DiagnosticsLayout.RowDefinitions.Count == 3)
            DiagnosticsLayout.RowDefinitions.RemoveAt(2);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await RefreshAsync();
    private async void OnMoreClick(object sender, RoutedEventArgs args) => await ViewModel.LoadAsync(reset: false);

    private async void OnCopyClick(object sender, RoutedEventArgs args)
    {
        try
        {
            string report = await BuildReportAsync();
            var data = new DataPackage();
            data.SetText(report);
            Clipboard.SetContent(data);
            ViewModel.StatusText = "诊断信息已复制";
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = "复制诊断失败：" + exception.Message;
            App.Services.Diagnostics.Error("复制诊断失败", exception);
        }
    }

    private async void OnExportLogClick(object sender, RoutedEventArgs args)
    {
        try
        {
            string path = Path.Combine(
                App.Services.Diagnostics.LogDirectory,
                $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(path, await BuildReportAsync(), Encoding.UTF8);
            ViewModel.StatusText = "诊断文件已导出：" + path;
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = "导出诊断失败：" + exception.Message;
            App.Services.Diagnostics.Error("导出诊断失败", exception);
        }
    }

    private void OnOpenLogDirectoryClick(object sender, RoutedEventArgs args)
    {
        string directory = App.Services.Diagnostics.LogDirectory;
        if (!Directory.Exists(directory))
        {
            ViewModel.StatusText = "日志目录不可用";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            ViewModel.StatusText = "已打开日志目录";
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = "打开日志目录失败：" + exception.Message;
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            ViewModel.Report = await BuildReportAsync();
            await ViewModel.LoadAsync(reset: true);
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = "读取诊断失败：" + exception.Message;
            App.Services.Diagnostics.Error("读取诊断失败", exception);
        }
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
        WorkspaceRuntime workspace = App.Services.Workspace;
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
        report.AppendLine($"窗口材质：Requested {FormatMaterial(workspace.RequestedAppearanceMaterial)} / Applied {FormatMaterial(workspace.AppliedAppearanceMaterial)}");
        report.AppendLine($"导航材质：{workspace.NavigationMaterial}");
        report.AppendLine($"透明强度：{FormatClarity(workspace.AppearanceClarity)}");
        report.AppendLine($"WebView ActualWidth：{FormatMetric(workspace.WebViewActualWidth)} DIP");
        report.AppendLine($"缩放模式：{workspace.WebViewZoomMode}");
        report.AppendLine($"页面缩放：{workspace.PageZoomPercent}%");
        report.AppendLine($"页面 scrollWidth：{FormatMetric(workspace.PageScrollWidth)} CSS px");
        report.AppendLine($"页面 viewport：{FormatMetric(workspace.PageViewportWidth)} CSS px");
        report.AppendLine($"Worker：{(App.Services.Worker.IsRunning ? "运行中" : "未启动")}");
        report.AppendLine($"本次任务：{session.Status} · {session.SavedCount} 题 · 进度 {session.Current}/{session.Total} · 重复 {session.DuplicateCount} · 异常 {session.ErrorCount} · AI 待处理 {session.AiPending} / 失败 {session.AiFailedCount}");
        report.AppendLine($"任务来源：{session.SourceUrl}");
        report.AppendLine($"最近导出：{latestExportText}");
        report.AppendLine($"日志路径：{App.Services.Diagnostics.LogPath}");
        report.AppendLine($"系统：{Environment.OSVersion.VersionString} · {Environment.Is64BitOperatingSystem switch { true => "x64", false => "x86" }} · {dpi}");
        report.AppendLine($"进程内存：{process.WorkingSet64 / 1024d / 1024d:0.0} MB · CPU 逻辑核心：{Environment.ProcessorCount}");
        report.AppendLine($"界面主题：{settings.AppearanceTheme} · 玻璃{(GlassSettings.IsSolid ? "已降级为实体" : $"着色 {GlassSettings.TintOpacity:P0}")}");
        report.AppendLine($"导出设置：{settings.DefaultExportFormat} · {(settings.ExportWithoutAnswers ? "练习版" : "答案版")}");
        return DiagnosticLog.Sanitize(report.ToString().TrimEnd());
    }

    private static string FormatMaterial(string value)
    {
        return AppearancePolicy.ResolveMaterial(value) switch
        {
            AppearanceMaterial.Mica => "Mica",
            AppearanceMaterial.Solid => "Solid",
            _ => "Acrylic",
        };
    }

    private static string FormatClarity(string value)
    {
        return AppearancePolicy.ResolveClarity(value) switch
        {
            AppearanceClarity.Clear => "clear",
            AppearanceClarity.Transparent => "transparent",
            _ => "standard",
        };
    }

    private static string FormatMetric(double value)
    {
        return double.IsFinite(value) && value > 0 ? $"{value:0.0}" : "未知";
    }
}
