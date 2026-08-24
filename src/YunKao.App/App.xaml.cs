using Microsoft.UI.Xaml;

namespace YunKao;

/// <summary>
/// 应用入口。第一阶段只负责创建 WinUI 窗口，不启动 Python Worker。
/// </summary>
public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow ??= new MainWindow();
        MainWindow.Activate();
    }
}
