using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using YunKao.Controls;
using YunKao.Core.Models;
using YunKao.Core.Services;
using YunKao.Services;
using YunKao.Views;
using Windows.Graphics;

namespace YunKao;

/// <summary>
/// 应用主窗口。页面内容通过 Frame 承载，避免导航状态散落在窗口布局中。
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureWindow();
        AppSettings settings = App.Services.Settings.Load();
        ApplyAppearance(settings.AppearanceMaterial, settings.AppearanceClarity);
        MotionService.SetReduceMotion(settings.ReduceMotion);

        NavigationRail.NavigationRequested += OnNavigationRequested;
        RootFrame.Navigated += OnRootFrameNavigated;
        RootSurface.SizeChanged += OnRootGridSizeChanged;
        RootFrame.Navigate(typeof(WorkspacePage));
    }

    /// <summary>
    /// 把设置页的材质选择立即应用到真实窗口。透明强度通过外壳 tint 控制，WebView 仍保持不透明。
    /// </summary>
    public void ApplyAppearance(string materialName, string clarityName)
    {
        BackdropMaterial requested = BackdropService.Parse(materialName);
        BackdropMaterial applied = BackdropService.Apply(this, RootSurface, requested);

        byte windowAlpha = clarityName switch
        {
            "clear" => 0x58,
            "transparent" => 0x28,
            _ => 0x40,
        };
        byte surfaceAlpha = clarityName switch
        {
            "clear" => 0xB2,
            "transparent" => 0x72,
            _ => 0x94,
        };

        if (applied == BackdropMaterial.Solid)
        {
            RootSurface.Background = (Brush)Application.Current.Resources["SolidWindowBackgroundBrush"];
            SetBrushColor("TitleBarBrush", 0xFF, 0xEE, 0xF2, 0xF6);
            SetBrushColor("RailSurfaceBrush", 0xFF, 0xF6, 0xF8, 0xFB);
            SetBrushColor("LiquidSurfaceBrush", 0xFF, 0xF8, 0xFA, 0xFC);
            SetBrushColor("LiquidSurfaceStrongBrush", 0xFF, 0xFB, 0xFC, 0xFE);
            SetBrushColor("LiquidSurfaceSubtleBrush", 0xFF, 0xF3, 0xF6, 0xF9);
            SetBrushColor("DrawerSurfaceBrush", 0xFF, 0xF8, 0xFA, 0xFC);
            return;
        }

        RootSurface.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(windowAlpha, 0xF0, 0xF7, 0xFC));
        SetBrushColor("TitleBarBrush", (byte)Math.Max(0x28, windowAlpha - 0x08), 0xF0, 0xF7, 0xFC);
        SetBrushColor("RailSurfaceBrush", surfaceAlpha, 0xF9, 0xFC, 0xFF);
        SetBrushColor("LiquidSurfaceBrush", (byte)Math.Max(0x62, surfaceAlpha - 0x1C), 0xF8, 0xFB, 0xFE);
        SetBrushColor("LiquidSurfaceStrongBrush", (byte)Math.Min(0xD2, surfaceAlpha + 0x12), 0xFB, 0xFD, 0xFF);
        SetBrushColor("LiquidSurfaceSubtleBrush", (byte)Math.Max(0x58, surfaceAlpha - 0x24), 0xF2, 0xF9, 0xFD);
        SetBrushColor("DrawerSurfaceBrush", (byte)Math.Min(0xDC, surfaceAlpha + 0x2E), 0xF8, 0xFC, 0xFE);
    }

    private static void SetBrushColor(string resourceKey, byte alpha, byte red, byte green, byte blue)
    {
        if (Application.Current.Resources[resourceKey] is SolidColorBrush brush)
        {
            brush.Color = Windows.UI.Color.FromArgb(alpha, red, green, blue);
        }
    }

    private void OnNavigationRequested(object? sender, NavigationRequestEventArgs args)
    {
        Type targetPage = args.Target switch
        {
            NavigationTarget.Workspace => typeof(WorkspacePage),
            NavigationTarget.History => typeof(HistoryPage),
            NavigationTarget.Export => typeof(ExportPage),
            NavigationTarget.Diagnostics => typeof(DiagnosticsPage),
            NavigationTarget.Settings => typeof(SettingsPage),
            _ => typeof(WorkspacePage),
        };

        if (RootFrame.CurrentSourcePageType == targetPage)
        {
            NavigationRail.SetActive(args.Target);
            return;
        }

        switch (args.Target)
        {
            case NavigationTarget.Workspace:
                RootFrame.Navigate(typeof(WorkspacePage));
                break;
            case NavigationTarget.History:
                RootFrame.Navigate(typeof(HistoryPage));
                break;
            case NavigationTarget.Export:
                RootFrame.Navigate(typeof(ExportPage));
                break;
            case NavigationTarget.Diagnostics:
                RootFrame.Navigate(typeof(DiagnosticsPage));
                break;
            case NavigationTarget.Settings:
                RootFrame.Navigate(typeof(SettingsPage));
                break;
        }

        NavigationRail.SetActive(args.Target);
        if (RootFrame.Content is WorkspacePage workspace) workspace.ApplyResponsiveLayout();
    }

    private void OnRootGridSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs args)
    {
        double width = args.NewSize.Width;
        double railWidth = width >= 1440 ? 68 : width >= 1100 ? 64 : 56;
        NavigationColumn.Width = new GridLength(railWidth);
        bool compact = width < 1440;
        NavigationRail.SetCompact(compact);
        if (RootFrame.Content is WorkspacePage workspace) workspace.ApplyResponsiveLayout();
    }

    private void OnRootFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
    {
        if (RootFrame.Content is Microsoft.UI.Xaml.FrameworkElement element)
        {
            // WebView2 原生 surface 不参与整页透明度/位移动画，避免切页时闪烁和重绘抖动。
            if (element is not WorkspacePage) MotionService.AnimatePage(element);
        }
    }

    private void ConfigureWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // 初始尺寸覆盖 1040×700 的布局验收基线，同时保留系统缩放能力。
            appWindow.Resize(new SizeInt32(1300, 850));
            AppWindow.SetIcon("Assets/AppIcon.ico");
        }
        catch (Exception)
        {
            // 设计时或无 AppWindow 的宿主环境不影响页面加载。
        }
    }

    /// <summary>
    /// 标题栏只显示当前会话的唯一状态，避免与工作台按钮出现相互矛盾的提示。
    /// </summary>
    public void SetTaskState(ExtractionStatus status)
    {
        (string text, string brushKey) = status switch
        {
            ExtractionStatus.Running => ("正在提取", "BluePrimaryBrush"),
            ExtractionStatus.Paused => ("任务已暂停", "AmberBrush"),
            ExtractionStatus.Completing => ("等待 AI 完成", "VioletPrimaryBrush"),
            ExtractionStatus.Completed => ("任务已完成", "CyanPrimaryBrush"),
            ExtractionStatus.Error => ("任务异常", "CoralPrimaryBrush"),
            _ => ("本地就绪", "CyanPrimaryBrush"),
        };
        TaskStatusText.Text = text;
        TaskStatusIndicator.Fill = (Brush)Application.Current.Resources[brushKey];
    }
}
