using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using YunKao.Controls;
using YunKao.Core.Models;
using YunKao.Core.Services;
using YunKao.Services;
using YunKao.ViewModels;
using YunKao.Views;

namespace YunKao;

/// <summary>
/// 应用外壳：彩色外框 + 侧边栏 + 内容卡片。网页常驻在内容卡片中，其他页面通过 Frame 覆盖其上，
/// 因此切换页面不会重建 WebView，登录状态始终保留。
/// </summary>
public sealed partial class MainWindow : Window
{
    private const double CompactWidth = 980;
    private const double ExpandedSidebarWidth = 250;
    private const double CompactSidebarWidth = 86;

    private static readonly Dictionary<NavigationTarget, Type> Pages = new()
    {
        [NavigationTarget.History] = typeof(HistoryPage),
        [NavigationTarget.Export] = typeof(ExportPage),
        [NavigationTarget.Diagnostics] = typeof(DiagnosticsPage),
        [NavigationTarget.Settings] = typeof(SettingsPage),
    };

    private readonly UISettings _uiSettings = new();
    private readonly LiquidGlassScene _paperScene;
    private readonly LiquidGlassScene _railScene;
    private AppWindow? _appWindow;
    private Action? _bannerAction;
    private bool _bannerReturnToWorkspace;
    private NavigationTarget _current = NavigationTarget.Workspace;
    private bool? _userCompact;
    private bool _compact;
    private bool _sidebarApplied;

    public MainWindow()
    {
        InitializeComponent();
        App.Services.Task.Dispatcher = DispatcherQueue;

        _paperScene = new LiquidGlassScene(PaperArtwork);
        _railScene = new LiquidGlassScene(RailScene);
        LiquidGlassScope.SetScene(RootSurface, _paperScene);
        LiquidGlassScope.SetScene(Sidebar, _railScene);
        NavigationRail.SetScene(_railScene);
        BrowserBar.ConnectGlass(_paperScene);
        Browser.ConnectGlass(_paperScene);
        Browser.ErrorVisibilityChanged += (_, _) => ArrangeIllustration();
        ButtonMotion.Attach(RootSurface);
        SidebarScroller.ViewChanged += (_, _) => { NavigationRail.RefreshLens(); SizeRailPortrait(); };
        GlassSettings.Changed += OnGlassSettingsChanged;
        Closed += (_, _) =>
        {
            GlassSettings.Changed -= OnGlassSettingsChanged;
            _paperScene.Dispose();
            _railScene.Dispose();
            _uiSettings.ColorValuesChanged -= OnSystemAppearanceChanged;
            _uiSettings.AdvancedEffectsEnabledChanged -= OnSystemAppearanceChanged;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                _uiSettings.AnimationsEnabledChanged -= OnSystemAppearanceChanged;
        };
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureWindow();

        AppSettings settings = App.Services.Settings.Load();
        ApplyTheme(settings.AppearanceTheme);
        ApplyAppearance(settings.AppearanceMaterial, settings.AppearanceClarity);
        MotionService.SetReduceMotion(settings.ReduceMotion);

        // 协调器登记在 AppServices 中，生命周期跟随应用。
        _ = new ExtractionCoordinator(App.Services, Browser);
        NavigationRail.NavigationRequested += (_, args) => ShowPage(args.Target);
        BrowserBar.Navigated += (_, _) => ShowPage(NavigationTarget.Workspace);
        App.Services.Browser.AddressEditRequested += (_, _) => BeginAddressEdit();
        App.Services.Browser.WorkspaceRequested += (_, _) => ShowPage(NavigationTarget.Workspace);
        RootFrame.Navigated += OnRootFrameNavigated;
        RootSurface.SizeChanged += OnRootSizeChanged;
        RootSurface.ActualThemeChanged += (_, _) => UpdateCaptionButtons();
        App.Services.Notifications.Requested += OnNotificationRequested;
        _uiSettings.AdvancedEffectsEnabledChanged += OnSystemAppearanceChanged;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            _uiSettings.AnimationsEnabledChanged += OnSystemAppearanceChanged;
        _uiSettings.ColorValuesChanged += OnSystemAppearanceChanged;
        RegisterShortcuts();
        NavigationRail.SetActive(NavigationTarget.Workspace);
    }

    // ---------------- 导航 ----------------

    /// <summary>切换内容卡片：工作台显示常驻网页，其余页面覆盖在网页之上。</summary>
    public void ShowPage(NavigationTarget target)
    {
        bool returningFromWorkspace = _current == NavigationTarget.Workspace;
        _current = target;
        ArrangeIllustration();
        NavigationRail.SetActive(target);
        if (target == NavigationTarget.Workspace)
        {
            RootFrame.Visibility = Visibility.Collapsed;
            WorkspaceContent.Visibility = Visibility.Visible;
            return;
        }

        Type page = Pages[target];
        WorkspaceContent.Visibility = Visibility.Collapsed;
        RootFrame.Visibility = Visibility.Visible;
        if (RootFrame.CurrentSourcePageType != page) RootFrame.Navigate(page);
        else if (returningFromWorkspace)
        {
            if (RootFrame.Content is HistoryPage history) _ = history.ViewModel.LoadAsync(reset: true);
            if (RootFrame.Content is ExportPage exports) _ = exports.ViewModel.LoadAsync(reset: true);
            if (RootFrame.Content is FrameworkElement element) MotionService.AnimatePage(element);
        }
    }

    private void OnRootFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
    {
        if (RootFrame.Content is FrameworkElement element) MotionService.AnimatePage(element);
    }

    private void BeginAddressEdit()
    {
        ShowPage(NavigationTarget.Workspace);
        BrowserBar.BeginAddressEdit();
    }

    private void RegisterShortcuts()
    {
        foreach ((VirtualKey key, VirtualKeyModifiers modifiers) in new[]
        {
            (VirtualKey.L, VirtualKeyModifiers.Control),
            (VirtualKey.R, VirtualKeyModifiers.Control),
            (VirtualKey.F5, VirtualKeyModifiers.None),
            (VirtualKey.Left, VirtualKeyModifiers.Menu),
            (VirtualKey.Right, VirtualKeyModifiers.Menu),
        })
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, args) =>
            {
                if (key == VirtualKey.L)
                {
                    BeginAddressEdit();
                    args.Handled = true;
                    return;
                }
                if (_current == NavigationTarget.Workspace)
                    args.Handled = Browser.TryHandleShortcut(key, modifiers);
            };
            RootSurface.KeyboardAccelerators.Add(accelerator);
        }
    }

    // ---------------- 侧边栏宽度 ----------------

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        ApplySidebarMode(args.NewSize.Width);
        ArrangeIllustration();
        UpdateTitleBarInsets();
    }

    private void OnCollapseClick(object sender, RoutedEventArgs args)
    {
        _userCompact = !_compact;
        ApplySidebarMode(RootSurface.ActualWidth);
    }

    private void ApplySidebarMode(double width)
    {
        bool compact = _userCompact ?? width < CompactWidth;
        if (_sidebarApplied && compact == _compact) return;
        _sidebarApplied = true;
        _compact = compact;
        SidebarColumn.Width = new GridLength(compact ? CompactSidebarWidth : ExpandedSidebarWidth);
        Sidebar.Padding = compact ? new Thickness(7, 12, 7, 12) : new Thickness(14, 14, 14, 12);
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TaskCard.SetCompact(compact);
        NavigationRail.SetCompact(compact);
        ArrangeIllustration();

        CollapseButton.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        CollapseGlyph.Glyph = compact ? "" : "";
        string tip = compact ? "展开侧栏" : "收起侧栏";
        ToolTipService.SetToolTip(CollapseButton, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CollapseButton, tip);
    }

    // ---------------- 外观 ----------------

    public void ApplyAppearance(string materialName, string clarityName)
    {
        string material = AppearancePolicy.NormalizeMaterial(materialName);
        string clarity = AppearancePolicy.NormalizeClarity(clarityName);
        BackdropMaterial applied = BackdropService.Apply(this, null, BackdropService.Parse(material));

        App.Services.Workspace.RequestedAppearanceMaterial = material;
        App.Services.Workspace.AppliedAppearanceMaterial = applied switch
        {
            BackdropMaterial.Mica => "mica",
            BackdropMaterial.Solid => "solid",
            _ => "acrylic",
        };
        App.Services.Workspace.AppearanceClarity = clarity;

        bool solid = applied == BackdropMaterial.Solid;
        RootSurface.Background = new SolidColorBrush(Colors.Transparent);

        FrameTint.Opacity = solid ? 1 : AppearancePolicy.ResolveClarity(clarity) switch
        {
            AppearanceClarity.Clear => 0.98,
            AppearanceClarity.Transparent => 0.76,
            _ => 0.91,
        };
        GlassSettings.Update(material, clarity, solid);
    }

    private void OnGlassSettingsChanged(object? sender, EventArgs args)
    {
        ArrangeIllustration();
        NavigationRail.RefreshLens();
    }

    private void OnSystemAppearanceChanged(UISettings sender, object args)
        => DispatcherQueue.TryEnqueue(() =>
        {
            ApplyAppearance(App.Services.Workspace.RequestedAppearanceMaterial, App.Services.Workspace.AppearanceClarity);
            MotionService.RefreshSystemSettings();
            UpdateCaptionButtons();
        });

    private void ArrangeIllustration()
    {
        bool illustrated = !GlassSettings.IsHighContrast;
        bool errorPortrait = _current == NavigationTarget.Workspace && Browser.IsShowingError;
        bool pagePortrait = illustrated && errorPortrait;
        double artWidth = errorPortrait ? Math.Clamp((RootSurface.ActualWidth - SidebarColumn.Width.Value) * .47, 180, 470)
            : Math.Clamp((RootSurface.ActualWidth - SidebarColumn.Width.Value) * .26, 230, 340);
        PagePortrait.Width = PagePortraitArt.Width = artWidth;
        PagePortrait.Height = Math.Min(artWidth * 1.43, Math.Max(160, RootSurface.ActualHeight - 108));
        PagePortrait.Visibility = pagePortrait ? Visibility.Visible : Visibility.Collapsed;
        RailPortraitCanvas.Visibility = illustrated && !pagePortrait ? Visibility.Visible : Visibility.Collapsed;
        SizeRailPortrait();
        RootFrame.Margin = new Thickness(0);
        PaperArtwork.Visibility = RailArtwork.Visibility = illustrated ? Visibility.Visible : Visibility.Collapsed;
        _paperScene.Invalidate();
    }

    private void OnPortraitAvailable(object sender, RoutedEventArgs args) => _paperScene?.Invalidate();
    private void OnRailPortraitAvailable(object sender, RoutedEventArgs args) => _railScene?.Invalidate();

    private void OnRailPortraitSizeChanged(object sender, SizeChangedEventArgs args) => SizeRailPortrait();

    private void SizeRailPortrait()
    {
        // Keep the character in one place across pages and window sizes. Fit the space between
        // navigation and task controls instead of sending it to the far corner of a wide window.
        if (RailPortraitArt is null || RailPortrait is null) return;
        double overlap = _compact ? 12 : 64;
        RailPortraitCanvas.Width = Math.Min(_compact ? 62 : 210, Math.Max(1, (RailPortrait.ActualHeight + overlap) / 1.5));
        Canvas.SetTop(RailPortraitArt, overlap - RailPortraitCanvas.Width * 1.5);
        // The half-length source ends behind the task card. Keep its pale clothes out of
        // the text area while the glass edge still samples the visible artwork above it.
        RailPortraitArt.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, RailPortraitCanvas.Width,
                Math.Max(1, RailPortraitCanvas.Width * 1.5 - overlap + 1)),
        };
        if (RailPortrait.XamlRoot is not null)
        {
            var origin = RailPortrait.TransformToVisual(RailScene).TransformPoint(new Windows.Foundation.Point());
            RailPortraitCanvas.Margin = new Thickness(origin.X + (RailPortrait.ActualWidth - RailPortraitCanvas.Width) / 2,
                origin.Y + RailPortrait.ActualHeight, 0, 0);
            _railScene?.Invalidate();
        }
    }

    public void ApplyTheme(string themeName)
    {
        RootSurface.RequestedTheme = AppearancePolicy.ResolveTheme(themeName) switch
        {
            AppearanceTheme.Light => ElementTheme.Light,
            AppearanceTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateCaptionButtons();
    }

    private void UpdateCaptionButtons()
    {
        if (_appWindow is null || !AppWindowTitleBar.IsCustomizationSupported()) return;
        bool dark = RootSurface.ActualTheme == ElementTheme.Dark;
        AppWindowTitleBar bar = _appWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = dark ? Windows.UI.Color.FromArgb(0xFF, 0xEC, 0xEA, 0xE6) : Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x1E, 0x1B);
        bar.ButtonHoverBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0x14, 0x1F, 0x1E, 0x1B);
        bar.ButtonHoverForegroundColor = bar.ButtonForegroundColor;
        bar.ButtonInactiveForegroundColor = dark
            ? Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0x80, 0x1F, 0x1E, 0x1B);
    }

    // ---------------- 全局横幅 ----------------

    private void OnNotificationRequested(object? sender, WorkspaceBannerInfo info)
    {
        void Show()
        {
            ShellBanner.Title = info.Title;
            ShellBanner.Message = info.Message;
            ShellBanner.Severity = info.Severity;
            _bannerAction = info.ActionCallback;
            _bannerReturnToWorkspace = info.ReturnToWorkspace;
            ShellBannerAction.Content = info.ActionLabel ?? "";
            ShellBannerAction.Visibility = string.IsNullOrWhiteSpace(info.ActionLabel) || info.ActionCallback is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            ShellBanner.IsOpen = true;
        }

        if (DispatcherQueue.HasThreadAccess) Show();
        else DispatcherQueue.TryEnqueue(Show);
    }

    private void OnShellBannerActionClick(object sender, RoutedEventArgs args)
    {
        ShellBanner.IsOpen = false;
        Action? action = _bannerAction;
        _bannerAction = null;
        if (_bannerReturnToWorkspace) ShowPage(NavigationTarget.Workspace);
        action?.Invoke();
    }

    // ---------------- 窗口 ----------------

    private void ConfigureWindow()
    {
        try
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));

            // 默认 1300×850；--window-size=宽x高 用于开发与验收复现固定尺寸（物理像素）。
            var initialSize = new SizeInt32(1300, 850);
            const string sizePrefix = "--window-size=";
            string? sizeArgument = Environment.GetCommandLineArgs()
                .FirstOrDefault(argument => argument.StartsWith(sizePrefix, StringComparison.Ordinal));
            string[]? dimensions = sizeArgument?[sizePrefix.Length..].Split('x', 2);
            if (dimensions?.Length == 2
                && int.TryParse(dimensions[0], out int width)
                && int.TryParse(dimensions[1], out int height)
                && width is >= 800 and <= 4096
                && height is >= 600 and <= 2160)
            {
                initialSize = new SizeInt32(width, height);
            }
            _appWindow.Resize(initialSize);
            _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        }
        catch (Exception)
        {
            // 设计时或无 AppWindow 的宿主环境不影响页面加载。
        }
    }

    private void UpdateTitleBarInsets()
    {
        if (_appWindow is null || RootSurface.XamlRoot is null) return;
        double scale = RootSurface.XamlRoot.RasterizationScale;
        if (scale <= 0) return;
        double right = _appWindow.TitleBar.RightInset / scale;
        double left = _appWindow.TitleBar.LeftInset / scale;
        AppTitleBar.Padding = new Thickness((_compact ? 31 : 24) + left, 8, Math.Max(12, right + 8), 0);
    }
}
