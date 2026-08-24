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

        WorkspaceButton.Background = target == NavigationTarget.Workspace ? active : inactive;
        HistoryButton.Background = target == NavigationTarget.History ? active : inactive;
        ExportButton.Background = target == NavigationTarget.Export ? active : inactive;
        DiagnosticsButton.Background = target == NavigationTarget.Diagnostics ? active : inactive;
        SettingsButton.Background = target == NavigationTarget.Settings ? active : inactive;
    }

    public void SetCompact(bool compact)
    {
        HeaderLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BrandLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SubtitleLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BaselineLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BaselineDescription.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HistoryLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ExportLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DiagnosticsLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SettingsLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

        Thickness padding = compact ? new Thickness(0, 10, 0, 10) : new Thickness(12, 10, 12, 10);
        foreach (Button button in new[] { WorkspaceButton, HistoryButton, ExportButton, DiagnosticsButton, SettingsButton })
        {
            button.Padding = padding;
            button.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }
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
