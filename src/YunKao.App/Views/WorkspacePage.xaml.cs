using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using YunKao.Controls;
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
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        Loaded += OnLoaded;
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

    public void ApplyWindowWidth(double windowWidth)
    {
        if (windowWidth <= 0) return;

        if (windowWidth >= 1180)
        {
            _narrow = false;
            _drawerOpen = false;
            ConfigureDrawerSurface(narrow: false);
            ControlColumn.Width = new GridLength(328);
            Grid.SetColumn(ControlPanel, 1);
            Grid.SetColumnSpan(ControlPanel, 1);
            ControlPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            ControlPanel.Visibility = Visibility.Visible;
            MotionService.SetTranslateX(ControlPanel, 0);
            ControlPanel.Opacity = 1;
            DrawerToggle.Visibility = Visibility.Collapsed;
            return;
        }

        if (windowWidth >= 960)
        {
            _narrow = false;
            _drawerOpen = false;
            ConfigureDrawerSurface(narrow: false);
            ControlColumn.Width = new GridLength(288);
            Grid.SetColumn(ControlPanel, 1);
            Grid.SetColumnSpan(ControlPanel, 1);
            ControlPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            ControlPanel.Visibility = Visibility.Visible;
            MotionService.SetTranslateX(ControlPanel, 0);
            ControlPanel.Opacity = 1;
            DrawerToggle.Visibility = Visibility.Collapsed;
            return;
        }

        bool enteringNarrowMode = !_narrow;
        _narrow = true;
        ConfigureDrawerSurface(narrow: true);
        ControlColumn.Width = new GridLength(0);
        Grid.SetColumn(ControlPanel, 0);
        Grid.SetColumnSpan(ControlPanel, 2);
        ControlPanel.Width = GetDrawerWidth();
        ControlPanel.HorizontalAlignment = HorizontalAlignment.Right;
        DrawerToggle.Visibility = Visibility.Visible;
        if (enteringNarrowMode)
        {
            _drawerOpen = false;
            ControlPanel.Visibility = Visibility.Collapsed;
            MotionService.SetTranslateX(ControlPanel, 32);
            ControlPanel.Opacity = 0;
        }
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
            bool scrimWasHidden = DrawerScrim.Visibility != Visibility.Visible;
            DrawerScrim.Visibility = Visibility.Visible;
            if (scrimWasHidden) DrawerScrim.Opacity = 0;
            MotionService.AnimateOpacity(DrawerScrim, 0.08, 160, "drawer-scrim");
            bool wasHidden = ControlPanel.Visibility != Visibility.Visible;
            ControlPanel.Visibility = Visibility.Visible;
            if (wasHidden)
            {
                MotionService.SetTranslateX(ControlPanel, 32);
                ControlPanel.Opacity = 0;
            }
            MotionService.AnimateTranslateAndOpacity(ControlPanel, 0, 1, 220);
        }
        else
        {
            if (DrawerScrim.Visibility == Visibility.Visible)
            {
                MotionService.AnimateOpacity(
                    DrawerScrim,
                    0,
                    160,
                    "drawer-scrim",
                    () =>
                    {
                        if (!_drawerOpen) DrawerScrim.Visibility = Visibility.Collapsed;
                    });
            }
            if (ControlPanel.Visibility != Visibility.Visible)
            {
                MotionService.SetTranslateX(ControlPanel, 32);
                ControlPanel.Opacity = 0;
                return;
            }

            MotionService.AnimateTranslateAndOpacity(
                ControlPanel,
                32,
                0,
                180,
                () =>
                {
                    if (!_drawerOpen) ControlPanel.Visibility = Visibility.Collapsed;
                });
        }
    }

    private void OnDrawerScrimTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
    {
        if (!_narrow || !_drawerOpen) return;
        _drawerOpen = false;
        UpdateDrawerVisual();
    }

    private void ConfigureDrawerSurface(bool narrow)
    {
        DrawerBackdrop.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        DrawerScrim.Visibility = narrow && _drawerOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!narrow) DrawerScrim.Opacity = 0;
        DrawerShell.Background = narrow
            ? (Brush)Application.Current.Resources["DrawerSurfaceBrush"]
            : (Brush)Application.Current.Resources["TransparentBrush"];
        DrawerShell.BorderBrush = narrow
            ? (Brush)Application.Current.Resources["DrawerBorderBrush"]
            : (Brush)Application.Current.Resources["TransparentBrush"];
        DrawerShell.BorderThickness = narrow ? new Thickness(1) : new Thickness(0);
        DrawerShell.Padding = narrow ? new Thickness(10) : new Thickness(0);
        MotionService.SetSoftShadow(DrawerShell, narrow);
        LiquidBackdropMode islandMode = narrow
            ? LiquidBackdropMode.Inherited
            : LiquidBackdropMode.System;
        TaskCard.BackdropMode = islandMode;
        ToolsCard.BackdropMode = islandMode;
        Events.BackdropMode = islandMode;
    }

    private double GetDrawerWidth()
    {
        double availableWidth = PageRoot.ActualWidth > 0
            ? PageRoot.ActualWidth - 76
            : 328;
        return Math.Clamp(availableWidth, 280, 328);
    }
}
