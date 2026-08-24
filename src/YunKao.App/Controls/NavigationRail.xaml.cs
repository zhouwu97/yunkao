using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YunKao.Services;
using Windows.Foundation;

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
    private NavigationTarget _activeTarget = NavigationTarget.Workspace;
    private bool _isLoaded;
    public event EventHandler<NavigationRequestEventArgs>? NavigationRequested;

    public NavigationRail()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SetActive(NavigationTarget.Workspace);
    }

    public void SetActive(NavigationTarget target)
    {
        bool changed = _activeTarget != target;
        _activeTarget = target;
        Brush transparent = (Brush)Application.Current.Resources["TransparentBrush"];
        WorkspaceButton.Background = transparent;
        HistoryButton.Background = transparent;
        ExportButton.Background = transparent;
        DiagnosticsButton.Background = transparent;
        SettingsButton.Background = transparent;
        WorkspaceButton.BorderBrush = transparent;
        HistoryButton.BorderBrush = transparent;
        ExportButton.BorderBrush = transparent;
        DiagnosticsButton.BorderBrush = transparent;
        SettingsButton.BorderBrush = transparent;
        if (_isLoaded) MoveSelectionBlob(changed);
    }

    public void SetCompact(bool compact)
    {
        // 68/64/60px rail 都使用同一套图标模式，避免在阈值附近抖动布局。
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = true;
        MotionService.SetSoftShadow(SelectionShape, enabled: true);
        MoveSelectionBlob(animate: false);
    }

    private void OnNavigationAreaSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_isLoaded) MoveSelectionBlob(animate: false);
    }

    private void MoveSelectionBlob(bool animate)
    {
        NavigationArea.UpdateLayout();
        Button target = GetButton(_activeTarget);
        Button first = WorkspaceButton;
        double firstY = first.TransformToVisual(NavigationArea).TransformPoint(new Point(0, 0)).Y;
        double targetY = target.TransformToVisual(NavigationArea).TransformPoint(new Point(0, 0)).Y;
        SelectionBlob.Margin = new Thickness(0, firstY, 0, 0);
        double translation = targetY - firstY;
        if (!animate)
        {
            SelectionTranslation.Y = translation;
            SelectionScale.ScaleX = 1;
            SelectionScale.ScaleY = 1;
            return;
        }

        MotionService.AnimateTranslateY(SelectionBlob, translation, 180);
        SelectionScale.ScaleX = 0.90;
        SelectionScale.ScaleY = 1.20;
        MotionService.AnimateScaleXY(SelectionShape, 1.08, 0.94, 90, () =>
            MotionService.AnimateScaleXY(SelectionShape, 1, 1, 130));
    }

    private Button GetButton(NavigationTarget target) => target switch
    {
        NavigationTarget.History => HistoryButton,
        NavigationTarget.Export => ExportButton,
        NavigationTarget.Diagnostics => DiagnosticsButton,
        NavigationTarget.Settings => SettingsButton,
        _ => WorkspaceButton,
    };

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
