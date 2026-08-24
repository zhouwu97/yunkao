using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using YunKao.Services;

namespace YunKao.Views;

public sealed partial class WorkspacePage : Page
{
    private ExtractionCoordinator? _coordinator;
    private bool _drawerOpen;
    private bool _narrow;

    public WorkspacePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        _coordinator ??= new ExtractionCoordinator(
            App.Services,
            Browser,
            Extraction,
            Progress,
            Export,
            AiStatus,
            Events);
    }

    private async void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if (_coordinator is null) return;
        await _coordinator.DisposeAsync();
        _coordinator = null;
    }

    public void ApplyWindowWidth(double windowWidth)
    {
        if (windowWidth <= 0) return;

        if (windowWidth >= 1180)
        {
            _narrow = false;
            _drawerOpen = false;
            ControlColumn.Width = new GridLength(328);
            Grid.SetColumn(ControlPanel, 1);
            Grid.SetColumnSpan(ControlPanel, 1);
            ControlPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            ControlPanel.Visibility = Visibility.Visible;
            DrawerToggle.Visibility = Visibility.Collapsed;
            return;
        }

        if (windowWidth >= 960)
        {
            _narrow = false;
            _drawerOpen = false;
            ControlColumn.Width = new GridLength(288);
            Grid.SetColumn(ControlPanel, 1);
            Grid.SetColumnSpan(ControlPanel, 1);
            ControlPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            ControlPanel.Visibility = Visibility.Visible;
            DrawerToggle.Visibility = Visibility.Collapsed;
            return;
        }

        _narrow = true;
        ControlColumn.Width = new GridLength(0);
        Grid.SetColumn(ControlPanel, 0);
        Grid.SetColumnSpan(ControlPanel, 2);
        ControlPanel.Width = 328;
        ControlPanel.HorizontalAlignment = HorizontalAlignment.Right;
        DrawerToggle.Visibility = Visibility.Visible;
        UpdateDrawerVisual();
    }

    private void OnDrawerToggleClick(object sender, RoutedEventArgs args)
    {
        if (!_narrow) return;
        _drawerOpen = !_drawerOpen;
        UpdateDrawerVisual();
    }

    private void UpdateDrawerVisual()
    {
        if (!_narrow) return;
        DrawerToggle.Label = _drawerOpen ? "收起任务" : "任务状态";
        if (_drawerOpen)
        {
            ControlPanel.Visibility = Visibility.Visible;
            ControlPanel.Opacity = 1;
            MotionService.AnimateTranslateX(ControlPanel, 0, 220);
        }
        else
        {
            MotionService.AnimateTranslateX(ControlPanel, 24, 180);
            ControlPanel.Visibility = Visibility.Collapsed;
        }
    }
}
