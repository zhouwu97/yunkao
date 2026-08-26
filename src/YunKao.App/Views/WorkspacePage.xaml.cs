using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using YunKao.Controls;
using YunKao.Core.Services;
using YunKao.Services;

namespace YunKao.Views;

public sealed partial class WorkspacePage : Page
{
    private ExtractionCoordinator? _coordinator;
    private bool _drawerOpen;
    private bool _narrow;
    private Action? _currentBannerAction;

    public WorkspacePage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        if (_coordinator is null)
        {
            _coordinator = new ExtractionCoordinator(
                App.Services,
                Browser,
                Extraction,
                Progress,
                Export,
                AiStatus,
                Events);
            _coordinator.BannerRequested += OnBannerRequested;
        }
    }

    private void OnBannerRequested(object? sender, WorkspaceBannerInfo info)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ActionBanner.Title = info.Title;
            ActionBanner.Message = info.Message;
            ActionBanner.Severity = info.Severity;
            _currentBannerAction = info.ActionCallback;
            BannerActionButton.Content = info.ActionLabel ?? "确定";
            BannerActionButton.Visibility = string.IsNullOrWhiteSpace(info.ActionLabel) ? Visibility.Collapsed : Visibility.Visible;
            ActionBanner.IsOpen = true;
        });
    }

    private void OnBannerActionButtonClick(object sender, RoutedEventArgs e)
    {
        ActionBanner.IsOpen = false;
        _currentBannerAction?.Invoke();
    }

    private void OnContentGridSizeChanged(object sender, SizeChangedEventArgs args)
    {
        ApplyResponsiveLayout();
    }

    /// <summary>
    /// 使用扣除导航栏和内边距的内容区宽度做预算。
    /// 只有保证浏览器最低舒适宽度 (>=1040px) 时才允许 Dock 右栏，
    /// 否则一律使用 Overlay Drawer，此时 ControlColumn.Width 为 0，打开/关闭面板完全不改变 WebView 宽度。
    /// </summary>
    public void ApplyResponsiveLayout()
    {
        if (ContentGrid.ActualWidth <= 0) return;

        WorkspaceLayoutMode layoutMode = WorkspaceLayoutBreakpoints.GetMode(ContentGrid.ActualWidth);
        if (layoutMode != WorkspaceLayoutMode.Narrow)
        {
            _narrow = false;
            _drawerOpen = false;
            double controlWidth = WorkspaceLayoutBreakpoints.GetControlWidth(layoutMode);
            ContentGrid.ColumnSpacing = WorkspaceLayoutBreakpoints.BrowserColumnSpacing;
            ControlColumn.Width = new GridLength(controlWidth);
            Grid.SetColumn(ControlPanel, 1);
            Grid.SetColumnSpan(ControlPanel, 1);
            ControlPanel.Width = controlWidth;
            ControlPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            ControlPanel.Visibility = Visibility.Visible;
            DrawerScrim.Visibility = Visibility.Collapsed;
            DrawerScrim.Opacity = 0;
            MotionService.SetTranslateX(ControlPanel, 0);
            ControlPanel.Opacity = 1;
            DrawerToggle.Visibility = Visibility.Collapsed;
            ConfigureDrawerSurface(narrow: false);
            return;
        }

        bool enteringNarrowMode = !_narrow;
        _narrow = true;
        ContentGrid.ColumnSpacing = 0;
        ControlColumn.Width = new GridLength(0); // 浏览器全宽，不被右栏挤压
        Grid.SetColumn(ControlPanel, 0);
        Grid.SetColumnSpan(ControlPanel, 2);
        ControlPanel.Width = GetDrawerWidth();
        ControlPanel.HorizontalAlignment = HorizontalAlignment.Right;
        DrawerToggle.Visibility = Visibility.Visible;
        ConfigureDrawerSurface(narrow: true);

        if (enteringNarrowMode)
        {
            _drawerOpen = false;
            ControlPanel.Visibility = Visibility.Collapsed;
            MotionService.SetTranslateX(ControlPanel, 24);
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
                MotionService.SetTranslateX(ControlPanel, 24);
                ControlPanel.Opacity = 0;
            }
            MotionService.AnimateTranslateAndOpacity(ControlPanel, 0, 1, 200);
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
                MotionService.SetTranslateX(ControlPanel, 24);
                ControlPanel.Opacity = 0;
                return;
            }

            MotionService.AnimateTranslateAndOpacity(
                ControlPanel,
                24,
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
            ? PageRoot.ActualWidth - 48
            : 344;
        return Math.Clamp(availableWidth, 280, 344);
    }
}
