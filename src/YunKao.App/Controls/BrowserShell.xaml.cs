using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using YunKao.Services;

namespace YunKao.Controls;

/// <summary>
/// WebView2 浏览器壳。它只负责浏览器生命周期和消息转发，不解析题目、不启动 Worker。
/// </summary>
public sealed partial class BrowserShell : UserControl
{
    private readonly WebViewService _service = new();
    private CompositionRoundedRectangleGeometry? _webClipGeometry;
    private CompositionGeometricClip? _webClip;
    private bool _loaded;
    private string _fullAddress = "https://www.cctrcloud.net/";

    public BrowserShell()
    {
        InitializeComponent();
        _service.NavigationChanged += OnNavigationChanged;
        _service.StatusChanged += OnStatusChanged;
        _service.ProcessFailed += OnProcessFailed;
        _service.BridgeMessageReceived += OnBridgeMessageReceived;
    }

    public WebViewService Service => _service;
    public event EventHandler<BridgeMessageEventArgs>? BridgeMessageReceived;
    public event EventHandler<string>? ProcessFailed;

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            await _service.InitializeAsync(WebView);
            _service.Navigate(new Uri("https://www.cctrcloud.net/"));
        }
        catch (WebViewInitializationException exception)
        {
            StatusText.Text = exception.Message + "，请安装 Evergreen Runtime。";
        }
        catch (Exception exception)
        {
            StatusText.Text = "浏览器启动失败：" + exception.Message;
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs args) => _service.Back();
    private void OnRefreshClick(object sender, RoutedEventArgs args) => _service.Refresh();
    private void OnExternalClick(object sender, RoutedEventArgs args) => _service.OpenExternal();

    private void OnAddressDisplayTapped(object sender, TappedRoutedEventArgs args)
    {
        BeginAddressEdit();
    }

    private void BeginAddressEdit()
    {
        AddressDisplay.Visibility = Visibility.Collapsed;
        AddressBox.Visibility = Visibility.Visible;
        AddressBox.Text = _fullAddress;
        AddressBox.Focus(FocusState.Programmatic);
        AddressBox.SelectAll();
    }

    private void OnAddressKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            OnAddressLostFocus(sender, args);
            return;
        }
        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        string input = AddressBox.Text.Trim();
        if (!input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) input = "https://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "地址必须是 HTTPS URL。";
            return;
        }

        try
        {
            _service.Navigate(uri);
            AddressBox.Visibility = Visibility.Collapsed;
            AddressDisplay.Visibility = Visibility.Visible;
            Focus(FocusState.Programmatic);
        }
        catch (Exception exception) { StatusText.Text = "导航失败：" + exception.Message; }
    }

    private void OnRootKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.L && args.KeyStatus.IsExtendedKey == false)
        {
            // Ctrl+L 是桌面浏览器约定的地址栏快捷键。
            if (IsModifierDown(VirtualKey.Control))
            {
                BeginAddressEdit();
                args.Handled = true;
                return;
            }
        }

        if (args.Key == VirtualKey.Left && args.KeyStatus.IsExtendedKey == false
            && IsModifierDown(VirtualKey.Menu))
        {
            _service.Back();
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.F5
            || (args.Key == VirtualKey.R
                && IsModifierDown(VirtualKey.Control)))
        {
            _service.Refresh();
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.Escape && AddressBox.Visibility == Visibility.Visible)
        {
            OnAddressLostFocus(sender, args);
            args.Handled = true;
        }
    }

    private void OnNavigationChanged(object? sender, BrowserNavigationEventArgs args)
    {
        _fullAddress = args.Uri?.AbsoluteUri ?? _fullAddress;
        string display = FormatAddress(args.Uri);
        AddressDisplay.Text = display;
        AddressBox.Text = display;
        ToolTipService.SetToolTip(AddressBox, _fullAddress);
        ToolTipService.SetToolTip(AddressDisplay, _fullAddress);
        StatusText.Text = args.Message;
    }

    private void OnAddressLostFocus(object sender, RoutedEventArgs args)
    {
        if (Uri.TryCreate(_fullAddress, UriKind.Absolute, out Uri? uri))
        {
            AddressDisplay.Text = FormatAddress(uri);
            AddressBox.Text = AddressDisplay.Text;
        }

        AddressBox.Visibility = Visibility.Collapsed;
        AddressDisplay.Visibility = Visibility.Visible;
    }

    private void OnWebViewSurfaceSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // Border 的圆角不一定能裁切 WebView2 原生 surface，因此在 Composition visual 上补真实圆角裁切。
        if (WebView.ActualWidth <= 0 || WebView.ActualHeight <= 0) return;

        Visual visual = ElementCompositionPreview.GetElementVisual(WebView);
        Compositor compositor = visual.Compositor;
        _webClipGeometry ??= compositor.CreateRoundedRectangleGeometry();
        _webClip ??= compositor.CreateGeometricClip(_webClipGeometry);
        _webClipGeometry.Size = new Vector2((float)WebView.ActualWidth, (float)WebView.ActualHeight);
        _webClipGeometry.CornerRadius = new Vector2(17, 17);
        visual.Clip = _webClip;
    }

    private static string FormatAddress(Uri? uri)
    {
        if (uri is null) return "cctrcloud.net";
        string path = string.IsNullOrWhiteSpace(uri.AbsolutePath) || uri.AbsolutePath == "/"
            ? ""
            : uri.AbsolutePath.TrimEnd('/');
        return string.IsNullOrWhiteSpace(path)
            ? uri.Host
            : $"{uri.Host} {path.Replace("/", " / ", StringComparison.Ordinal)}";
    }

    private void OnStatusChanged(object? sender, string message) => StatusText.Text = message;
    private void OnProcessFailed(object? sender, string message)
    {
        StatusText.Text = message + "，正在重建 WebView2。";
        ProcessFailed?.Invoke(this, message);
    }

    private void OnBridgeMessageReceived(object? sender, BridgeMessageEventArgs args)
    {
        BridgeMessageReceived?.Invoke(this, args);
    }

    private static bool IsModifierDown(VirtualKey key)
    {
        return InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    }
}
