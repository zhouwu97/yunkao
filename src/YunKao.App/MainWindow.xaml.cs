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
        BackdropService.Apply(this, BackdropMaterial.DesktopAcrylic);

        NavigationRail.NavigationRequested += OnNavigationRequested;
        RootFrame.Navigate(typeof(WorkspacePage));
    }

    private void OnNavigationRequested(object? sender, NavigationRequestEventArgs args)
    {
        switch (args.Target)
        {
            case NavigationTarget.Workspace:
                RootFrame.Navigate(typeof(WorkspacePage));
                break;
            case NavigationTarget.Settings:
                RootFrame.Navigate(typeof(SettingsPage));
                break;
        }

        NavigationRail.SetActive(args.Target);
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
