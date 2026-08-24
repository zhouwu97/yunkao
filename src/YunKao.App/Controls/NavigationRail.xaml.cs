using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace YunKao.Controls;

public enum NavigationTarget
{
    Workspace,
    History,
    Export,
    Diagnostics,
    Settings
}

public sealed class NavigationRequestEventArgs(NavigationTarget target) : EventArgs
{
    public NavigationTarget Target { get; } = target;
}

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
        var activeBorder = (Brush)Application.Current.Resources["LiquidBorderBrush"];

        WorkspaceButton.Background = target == NavigationTarget.Workspace ? active : inactive;
        HistoryButton.Background = target == NavigationTarget.History ? active : inactive;
        ExportButton.Background = target == NavigationTarget.Export ? active : inactive;
        DiagnosticsButton.Background = target == NavigationTarget.Diagnostics ? active : inactive;
        SettingsButton.Background = target == NavigationTarget.Settings ? active : inactive;
        WorkspaceButton.BorderBrush = target == NavigationTarget.Workspace ? activeBorder : inactive;
        HistoryButton.BorderBrush = target == NavigationTarget.History ? activeBorder : inactive;
        ExportButton.BorderBrush = target == NavigationTarget.Export ? activeBorder : inactive;
        DiagnosticsButton.BorderBrush = target == NavigationTarget.Diagnostics ? activeBorder : inactive;
        SettingsButton.BorderBrush = target == NavigationTarget.Settings ? activeBorder : inactive;
    }

    public void SetCompact(bool compact)
    {
        // 68/64/60px rail 都使用同一套图标模式，避免在阈值附近抖动布局。
    }

    private void OnWorkspaceClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, new NavigationRequestEventArgs(NavigationTarget.Workspace));
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, new NavigationRequestEventArgs(NavigationTarget.Settings));
    }

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, new NavigationRequestEventArgs(NavigationTarget.History));
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, new NavigationRequestEventArgs(NavigationTarget.Export));
    }

    private void OnDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, new NavigationRequestEventArgs(NavigationTarget.Diagnostics));
    }
}
