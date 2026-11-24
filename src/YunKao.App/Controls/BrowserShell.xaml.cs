using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using YunKao.Core.Services;
using YunKao.Services;
using YunKao.ViewModels;

namespace YunKao.Controls;

/// <summary>
/// WebView2 宿主。负责浏览器生命周期、页面宽度适配和消息转发；
/// 地址、导航状态通过 <see cref="BrowserViewModel"/> 同步到侧边栏。不解析题目、不启动 Worker。
/// </summary>
public sealed partial class BrowserShell : UserControl, IBrowserActions
{
    private readonly WebViewService _service = new();
    private readonly SemaphoreSlim _fitWidthGate = new(1, 1);
    private CancellationTokenSource? _fitWidthResizeCts;
    private bool _loaded;
    private bool _fitWidth = true;
    private long _navigationRevision;
    private CompositionRoundedRectangleGeometry? _clipGeometry;

    public BrowserShell()
    {
        InitializeComponent();
        _service.NavigationChanged += OnNavigationChanged;
        _service.LoadingChanged += (_, loading) =>
        {
            LoadingProgress.IsIndeterminate = loading;
            LoadingProgress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            if (loading)
            {
                ++_navigationRevision;
                _fitWidthResizeCts?.Cancel();
                NavigationErrorOverlay.Visibility = Visibility.Collapsed;
                UpdateErrorSurface();
            }
        };
        _service.StatusChanged += OnStatusChanged;
        _service.ProcessFailed += OnProcessFailed;
        _service.BridgeMessageReceived += (_, args) => BridgeMessageReceived?.Invoke(this, args);
        _service.AddressRequested += (_, _) => DispatcherQueue.TryEnqueue(ViewModel.RequestAddressEdit);
        ViewModel.Attach(this);
    }

    public bool IsShowingError => NavigationErrorOverlay.Visibility == Visibility.Visible || WebViewErrorOverlay.Visibility == Visibility.Visible;
    internal event EventHandler? ErrorVisibilityChanged;

    internal void ConnectGlass(LiquidGlassScene scene)
    {
        ErrorGlass.Connect(scene, NavigationErrorOverlay);
        RuntimeGlass.Connect(scene, WebViewErrorOverlay);
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args)
    {
        double width = args.NewSize.Width;
        double inset = width < 580 ? 12 : Math.Clamp(width * .065, 26, 92);
        NavigationErrorOverlay.Margin = WebViewErrorOverlay.Margin = new Thickness(inset, 12, inset, 12);
        NavigationErrorOverlay.MaxWidth = WebViewErrorOverlay.MaxWidth = Math.Clamp(width * .52, 275, 420);
        ErrorGlass.RefreshPosition();
        RuntimeGlass.RefreshPosition();
    }

    private void UpdateErrorSurface()
    {
        bool error = IsShowingError;
        WebContent.Visibility = error ? Visibility.Collapsed : Visibility.Visible;
        ErrorVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public WebViewService Service => _service;
    public BrowserViewModel ViewModel => App.Services.Browser;
    public event EventHandler<BridgeMessageEventArgs>? BridgeMessageReceived;
    public event EventHandler<string>? ProcessFailed;

    private WorkspaceRuntime Workspace => App.Services.Workspace;

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        await InitializeBrowserAsync();
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            WebViewErrorOverlay.Visibility = Visibility.Collapsed;
            UpdateErrorSurface();
            await _service.InitializeAsync(WebView);
            Workspace.WebViewInitialized = true;
            Workspace.BrowserVersion = _service.BrowserVersion;
            SetStatus("WebView2 已就绪");
            _service.GoHome();
            if (XamlRoot is { } root && FocusManager.GetFocusedElement(root) is null)
                WebView.Focus(FocusState.Programmatic);
        }
        catch (WebViewInitializationException exception)
        {
            SetStatus(exception.Message + "，请安装 Evergreen Runtime。");
            ErrorMessageText.Text = "融智云考需要 Microsoft Edge WebView2 Runtime。当前系统尚未安装或版本过低，请点击下方按钮下载安装。";
            WebViewErrorOverlay.Visibility = Visibility.Visible;
            UpdateErrorSurface();
        }
        catch (Exception exception)
        {
            SetStatus("浏览器启动失败：" + exception.Message);
            ErrorMessageText.Text = "浏览器启动失败：" + exception.Message;
            WebViewErrorOverlay.Visibility = Visibility.Visible;
            UpdateErrorSurface();
        }
    }

    // ---------------- IBrowserActions ----------------

    public void Back() => _service.Back();
    public void Forward() => _service.Forward();
    public void Refresh() => _service.Refresh();
    public void Home() => _service.GoHome();
    public void OpenExternal() => _service.OpenExternal();

    public async void ToggleFitWidth()
    {
        _fitWidth = !_fitWidth;
        _fitWidthResizeCts?.Cancel();
        if (_fitWidth) await ApplyFitWidthAsync();
        else await ApplyOriginalZoomAsync();
    }

    public string? Navigate(string input)
    {
        input = (input ?? "").Trim();
        if (input.Length == 0) return "请输入网址";
        if (input.Contains("://", StringComparison.Ordinal)
            && !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "地址必须是 HTTPS URL。";
        }
        if (!input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) input = "https://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return "地址必须是 HTTPS URL。";
        }

        try
        {
            _service.Navigate(uri);
            return null;
        }
        catch (Exception exception)
        {
            return "导航失败：" + exception.Message;
        }
    }

    internal bool TryHandleShortcut(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (key == VirtualKey.L && modifiers == VirtualKeyModifiers.Control)
        {
            ViewModel.RequestAddressEdit();
            return true;
        }
        if (key == VirtualKey.Left && modifiers == VirtualKeyModifiers.Menu)
        {
            _service.Back();
            return true;
        }
        if (key == VirtualKey.Right && modifiers == VirtualKeyModifiers.Menu)
        {
            _service.Forward();
            return true;
        }
        if ((key == VirtualKey.F5 && modifiers == VirtualKeyModifiers.None)
            || (key == VirtualKey.R && modifiers == VirtualKeyModifiers.Control))
        {
            _service.Refresh();
            return true;
        }
        return false;
    }

    // ---------------- 浏览器事件 ----------------

    private async void OnNavigationChanged(object? sender, BrowserNavigationEventArgs args)
    {
        ++_navigationRevision;
        ViewModel.SetNavigationState(_service.CanGoBack, _service.CanGoForward);
        ViewModel.SetAddress(args.Uri);
        SetStatus(args.Message);
        Workspace.CurrentUrl = args.Uri;
        Workspace.BridgeInstalled = _service.IsBridgeInstalled;
        Workspace.BrowserVersion = _service.BrowserVersion;
        if (!args.Success)
        {
            NavigationErrorText.Text = args.Message;
            NavigationErrorOverlay.Visibility = Visibility.Visible;
            UpdateErrorSurface();
            _fitWidthResizeCts?.Cancel();
        }
        if (args.Message == "导航完成")
        {
            Workspace.PageScrollWidth = 0;
            Workspace.PageViewportWidth = 0;
            Workspace.PageZoomPercent = 100;
        }
        Workspace.WebViewZoomMode = _fitWidth ? "FitWidth" : "100%";
        if (args.Success && args.Message == "导航完成" && _fitWidth)
        {
            await ApplyFitWidthAsync();
        }
    }

    private void OnStatusChanged(object? sender, string message)
    {
        SetStatus(message);
        Workspace.BridgeInstalled = _service.IsBridgeInstalled;
    }

    private void OnProcessFailed(object? sender, string message)
    {
        SetStatus(message + "，正在重建 WebView2。");
        Workspace.WebViewInitialized = false;
        Workspace.BridgeInstalled = false;
        ProcessFailed?.Invoke(this, message);
    }

    private void SetStatus(string message)
    {
        ViewModel.StatusText = message;
        Workspace.BrowserStatus = message;
    }

    private void OnInstallWebView2Click(object sender, RoutedEventArgs args)
        => Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true });

    private async void OnRetryWebView2Click(object sender, RoutedEventArgs args) => await InitializeBrowserAsync();
    private void OnReloadClick(object sender, RoutedEventArgs args) => Refresh();
    private void OnHomeClick(object sender, RoutedEventArgs args) => Home();

    // ---------------- 页面宽度适配 ----------------

    private async Task ApplyOriginalZoomAsync()
    {
        if (_service.IsInitialized && WebViewService.IsAllowedHost(_service.CurrentUri))
        {
            await _fitWidthGate.WaitAsync();
            try
            {
                await _service.ExecuteScriptAsync("document.getElementById('__yunkaoLoginLayout')?.remove(); document.documentElement.style.zoom = '100%';");
                Workspace.WebViewZoomMode = "100%";
                Workspace.PageZoomPercent = 100;
            }
            catch (Exception exception)
            {
                App.Services.Diagnostics.Warning("恢复页面原始缩放失败：" + exception.Message);
            }
            finally
            {
                _fitWidthGate.Release();
            }
        }

        ViewModel.ZoomLabel = "原始大小 100%";
        SetStatus("页面已恢复为 100% 缩放");
    }

    private async Task ApplyFitWidthAsync(CancellationToken cancellationToken = default, bool fromResize = false)
    {
        if (IsShowingError || _service.IsLoading || !_service.IsInitialized || !WebViewService.IsAllowedHost(_service.CurrentUri)) return;

        bool gateEntered = false;
        long revision = _navigationRevision;
        try
        {
            await _fitWidthGate.WaitAsync(cancellationToken);
            gateEntered = true;
            if (!_fitWidth) return;

            BrowserPageState pageState = await _service.GetPageStateAsync(cancellationToken);
            const string measureScript = """
                (() => {
                    const root = document.documentElement;
                    const previous = root.style.zoom;
                    try {
                        root.style.zoom = '100%';
                        const viewport = Math.max(root.clientWidth, window.innerWidth || 0);
                        const login = document.querySelector('.login_layouts');
                        let layoutStyle = document.getElementById('__yunkaoLoginLayout');
                        if (login && viewport < 860) {
                            if (!layoutStyle) {
                                layoutStyle = document.createElement('style');
                                layoutStyle.id = '__yunkaoLoginLayout';
                                document.head.appendChild(layoutStyle);
                            }
                            layoutStyle.textContent = '.login_layouts{width:600px!important;margin-left:-300px!important;height:700px!important;margin-top:-350px!important}.login_cont_left{display:none!important}';
                        } else {
                            layoutStyle?.remove();
                        }
                        const rect = login?.getBoundingClientRect();
                        const width = Math.max(root.scrollWidth, document.body?.scrollWidth || 0, rect?.width || 0);
                        return [width, viewport, Math.max(0, window.innerHeight - 24), rect?.height || 0].join(':');
                    } finally {
                        root.style.zoom = previous;
                    }
                })();
                """;
            string raw = await _service.ExecuteScriptAsync(measureScript, cancellationToken);
            string value = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? "";
            string[] dimensions = value.Split(':');
            if (dimensions.Length != 4
                || !double.TryParse(dimensions[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double scrollWidth)
                || !double.TryParse(dimensions[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double viewportWidth)
                || scrollWidth <= 0
                || viewportWidth <= 0)
            {
                return;
            }

            double.TryParse(dimensions[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double viewportHeight);
            double.TryParse(dimensions[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double contentHeight);
            BrowserZoomResult result = BrowserZoomCalculator.Calculate(viewportWidth, scrollWidth, pageState, viewportHeight, contentHeight);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsShowingError || !_fitWidth || revision != _navigationRevision) return;
            await _service.ExecuteScriptAsync($"document.documentElement.style.zoom = '{result.Percent}%';", cancellationToken);
            if (IsShowingError || !_fitWidth || revision != _navigationRevision) return;

            Workspace.WebViewActualWidth = WebView.ActualWidth;
            Workspace.PageScrollWidth = result.ScrollWidth;
            Workspace.PageViewportWidth = result.ViewportWidth;
            Workspace.WebViewZoomMode = "FitWidth";
            Workspace.PageZoomPercent = result.Percent;
            ViewModel.ZoomLabel = $"适应宽度 {result.Percent}%";
            SetStatus(result.Percent < 100
                ? $"已缩放至 {result.Percent}% 以适应宽度"
                : "页面完整显示");
        }
        catch (OperationCanceledException)
        {
            // 窗口继续变化时，丢弃过期的测量结果。
        }
        catch (Exception exception)
        {
            App.Services.Diagnostics.Warning("页面宽度适配失败：" + exception.Message);
        }
        finally
        {
            if (gateEntered) _fitWidthGate.Release();
        }
    }

    /// <summary>WebView2 原生 surface 不受父级圆角裁切，在 Composition visual 上补圆角，与内容卡片一致。</summary>
    private void ApplyRoundedClip()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(WebView);
        if (_clipGeometry is null)
        {
            _clipGeometry = visual.Compositor.CreateRoundedRectangleGeometry();
            _clipGeometry.CornerRadius = new Vector2(13, 13);
            visual.Clip = visual.Compositor.CreateGeometricClip(_clipGeometry);
        }
        _clipGeometry.Size = new Vector2((float)WebView.ActualWidth, (float)WebView.ActualHeight);
    }

    private async void OnWebViewSurfaceSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (WebView.ActualWidth <= 0 || WebView.ActualHeight <= 0) return;
        ApplyRoundedClip();
        Workspace.WebViewActualWidth = WebView.ActualWidth;
        if (!_fitWidth || !_service.IsInitialized) return;

        _fitWidthResizeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _fitWidthResizeCts = cts;
        try
        {
            await Task.Delay(120, cts.Token);
            await ApplyFitWidthAsync(cts.Token, fromResize: true);
        }
        catch (OperationCanceledException)
        {
            // 仅保留最后一次尺寸变化的适配请求。
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(_fitWidthResizeCts, cts)) _fitWidthResizeCts = null;
        }
    }
}
