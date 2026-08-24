using Microsoft.UI.Xaml;
using YunKao.Services;

namespace YunKao;

/// <summary>
/// 应用入口。全局服务在窗口生命周期内共享，并在窗口关闭时统一释放。
/// </summary>
public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }
    public static AppServices Services { get; } = new();
    private bool _servicesDisposed;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = Services.InitializeAsync();
        if (MainWindow is null)
        {
            MainWindow = new MainWindow();
            MainWindow.Closed += OnMainWindowClosed;
        }
        MainWindow.Activate();
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_servicesDisposed) return;
        _servicesDisposed = true;
        await Services.DisposeAsync();
    }
}
