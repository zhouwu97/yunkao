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
            Browser.TaskRequested += OnTaskRequested;
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
    /// 任务面板始终覆盖在 WebView 上，打开或关闭都不会改变网页视口宽度。
    /// </summary>
    public void ApplyResponsiveLayout()
    {
        if (ContentGrid.ActualWidth <= 0) return;

        bool enteringNarrowMode = !_narrow;
        _narrow = true;
        ContentGrid.ColumnSpacing = 0;
        ControlColumn.Width = new GridLength(0); // 浏览器全宽，不被右栏挤压
        Grid.SetColumn(ControlPanel, 0);
        Grid.SetColumnSpan(ControlPanel, 2);
        ControlPanel.Width = GetDrawerWidth();
        ControlPanel.HorizontalAlignment = HorizontalAlignment.Right;
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

    private void OnTaskRequested(object? sender, EventArgs args)
    {
        _drawerOpen = !_drawerOpen;
        UpdateDrawerVisual();
    }

    private void OnDrawerCloseClick(object sender, RoutedEventArgs args)
    {
        if (!_drawerOpen) return;
        _drawerOpen = false;
        UpdateDrawerVisual();
    }

    private void UpdateDrawerVisual()
    {
        if (!_narrow) return;
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
        Events.BackdropMode = LiquidBackdropMode.Inherited;
    }

    private double GetDrawerWidth()
    {
        double availableWidth = PageRoot.ActualWidth > 0
            ? PageRoot.ActualWidth - 48
            : 344;
        return Math.Clamp(availableWidth, 280, 344);
    }
}
