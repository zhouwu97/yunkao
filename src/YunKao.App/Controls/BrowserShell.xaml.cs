using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using YunKao.Services;

namespace YunKao.Controls;

/// <summary>
/// WebView2 浏览器壳。它只负责浏览器生命周期和消息转发，不解析题目、不启动 Worker。
/// </summary>
public sealed partial class BrowserShell : UserControl
{
    private readonly WebViewService _service = new();
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

    private void OnAddressKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
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

        try { _service.Navigate(uri); }
        catch (Exception exception) { StatusText.Text = "导航失败：" + exception.Message; }
    }

    private void OnNavigationChanged(object? sender, BrowserNavigationEventArgs args)
    {
        _fullAddress = args.Uri?.AbsoluteUri ?? _fullAddress;
        AddressBox.Text = FormatAddress(args.Uri);
        ToolTipService.SetToolTip(AddressBox, _fullAddress);
        StatusText.Text = args.Message;
    }

    private void OnAddressGotFocus(object sender, RoutedEventArgs args)
    {
        AddressBox.Text = _fullAddress;
        AddressBox.SelectAll();
    }

    private void OnAddressLostFocus(object sender, RoutedEventArgs args)
    {
        if (Uri.TryCreate(_fullAddress, UriKind.Absolute, out Uri? uri)) AddressBox.Text = FormatAddress(uri);
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
    private void OnProcessFailed(object? sender, string message) => StatusText.Text = message + "，可刷新恢复。";

    private void OnBridgeMessageReceived(object? sender, BridgeMessageEventArgs args)
    {
        BridgeMessageReceived?.Invoke(this, args);
    }
}
