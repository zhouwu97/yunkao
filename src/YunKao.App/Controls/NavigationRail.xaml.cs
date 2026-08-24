using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace YunKao.Controls;

public enum NavigationTarget
{
    Workspace,
    Settings
}

public sealed class NavigationRequestEventArgs(NavigationTarget target) : EventArgs
{
    public NavigationTarget Target { get; } = target;
}

/// <summary>
/// 第一阶段只保留已承诺的工作台与设置入口，避免创建尚未实现的假页面。
/// </summary>
public sealed partial class NavigationRail : UserControl
{
    public event EventHandler<NavigationRequestEventArgs>? NavigationRequested;

    public NavigationRail()
    {
        InitializeComponent();
        SetActive(NavigationTarget.Workspace);
    }

    public void SetActive(NavigationTarget target)
    {
        var active = (Brush)Application.Current.Resources["ActiveNavigationBrush"];
        var inactive = (Brush)Application.Current.Resources["TransparentBrush"];

        WorkspaceButton.Background = target == NavigationTarget.Workspace ? active : inactive;
        SettingsButton.Background = target == NavigationTarget.Settings ? active : inactive;
    }

    private void OnWorkspaceClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, new NavigationRequestEventArgs(NavigationTarget.Workspace));
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, new NavigationRequestEventArgs(NavigationTarget.Settings));
    }
}
