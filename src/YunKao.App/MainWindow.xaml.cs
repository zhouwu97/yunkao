using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using YunKao.Controls;
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
        BackdropMaterial material = BackdropService.Apply(this, RootSurface, BackdropMaterial.DesktopAcrylic);
        RootSurface.Background = material == BackdropMaterial.Solid
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidWindowBackgroundBrush"]
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

        NavigationRail.NavigationRequested += OnNavigationRequested;
        RootFrame.Navigated += OnRootFrameNavigated;
        RootSurface.SizeChanged += OnRootGridSizeChanged;
        RootFrame.Navigate(typeof(WorkspacePage));
    }

    private void OnNavigationRequested(object? sender, NavigationRequestEventArgs args)
    {
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
        if (RootFrame.Content is WorkspacePage workspace) workspace.ApplyWindowWidth(RootSurface.ActualWidth);
    }

    private void OnRootGridSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs args)
    {
        double width = args.NewSize.Width;
        double railWidth = width >= 1180 ? 68 : width >= 960 ? 64 : 60;
        NavigationColumn.Width = new GridLength(railWidth);
        bool compact = width < 1180;
        NavigationRail.SetCompact(compact);
        if (RootFrame.Content is WorkspacePage workspace) workspace.ApplyWindowWidth(width);
    }

    private void OnRootFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
    {
        if (RootFrame.Content is Microsoft.UI.Xaml.FrameworkElement element)
        {
            MotionService.AnimatePage(element);
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
}
