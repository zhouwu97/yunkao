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
    private const double CompactWidth = 1100;
    private const double ExpandedSidebarWidth = 248;
    private const double CompactSidebarWidth = 78;

    private static readonly Dictionary<NavigationTarget, Type> Pages = new()
    {
        [NavigationTarget.History] = typeof(HistoryPage),
        [NavigationTarget.Export] = typeof(ExportPage),
        [NavigationTarget.Diagnostics] = typeof(DiagnosticsPage),
        [NavigationTarget.Settings] = typeof(SettingsPage),
    };

    private readonly UISettings _uiSettings = new();
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
        _uiSettings.AdvancedEffectsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(GlassSettings.Refresh);
        RegisterShortcuts();
        NavigationRail.SetActive(NavigationTarget.Workspace);
    }

    // ---------------- 导航 ----------------

    /// <summary>切换内容卡片：工作台显示常驻网页，其余页面覆盖在网页之上。</summary>
    public void ShowPage(NavigationTarget target)
    {
        _current = target;
        NavigationRail.SetActive(target);
        if (target == NavigationTarget.Workspace)
        {
            RootFrame.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
            return;
        }

        Type page = Pages[target];
        Browser.Visibility = Visibility.Collapsed;
        RootFrame.Visibility = Visibility.Visible;
        if (RootFrame.CurrentSourcePageType != page) RootFrame.Navigate(page);
        else if (RootFrame.Content is FrameworkElement element) MotionService.AnimatePage(element);
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
        Sidebar.Padding = compact ? new Thickness(10, 6, 6, 14) : new Thickness(14, 6, 10, 14);
        BrowserBar.SetCompact(compact);
        TaskCard.SetCompact(compact);
        NavigationRail.SetCompact(compact);
        PagesLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TaskCard.Margin = compact ? new Thickness(0, 10, 0, 6) : new Thickness(0, 14, 0, 0);
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
        // 纸面默认几乎不透明；选"通透"时才让桌面材质轻微透出。
        FrameTint.Opacity = solid ? 1 : AppearancePolicy.ResolveClarity(clarity) switch
        {
            AppearanceClarity.Clear => 1.0,
            AppearanceClarity.Transparent => 0.82,
            _ => 0.96,
        };
        GlassSettings.Update(material, clarity, solid);
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
            _appWindow.SetIcon("Assets/AppIcon.ico");
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
        AppTitleBar.Padding = new Thickness(18 + left, 0, Math.Max(12, right + 8), 0);
    }
}
