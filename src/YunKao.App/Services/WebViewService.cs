using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace YunKao.Services;

public sealed class BrowserNavigationEventArgs(Uri? uri, bool success, string message) : EventArgs
{
    public Uri? Uri { get; } = uri;
    public bool Success { get; } = success;
    public string Message { get; } = message;
}

public sealed class BridgeMessageEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

/// <summary>
/// WebView2 生命周期、安全域名和 JS Bridge 的边界。
/// </summary>
public sealed class WebViewService : IAsyncDisposable
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cctrcloud.net",
        "www.cctrcloud.net",
    };

    private WebView2? _view;
    private CoreWebView2? _core;
    private string? _bridgeScript;

    public bool IsInitialized => _core is not null;
    public bool IsBridgeInstalled { get; private set; }
    public Uri? CurrentUri { get; private set; }

    public event EventHandler<BrowserNavigationEventArgs>? NavigationChanged;
    public event EventHandler<BridgeMessageEventArgs>? BridgeMessageReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ProcessFailed;

    public async Task InitializeAsync(WebView2 view, CancellationToken cancellationToken = default)
    {
        _view = view;
        try
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YunKaoDesktop",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync().AsTask(cancellationToken);
            await view.EnsureCoreWebView2Async(environment).AsTask(cancellationToken);
            _core = view.CoreWebView2;
            _core.NavigationCompleted += OnNavigationCompleted;
            _core.SourceChanged += OnSourceChanged;
            _core.WebMessageReceived += OnWebMessageReceived;
            _core.ProcessFailed += OnProcessFailed;
            _core.Settings.IsStatusBarEnabled = false;
#if !DEBUG
            _core.Settings.AreDevToolsEnabled = false;
#endif
            _bridgeScript = await LoadBridgeScriptAsync().ConfigureAwait(true);
            StatusChanged?.Invoke(this, "WebView2 已就绪");
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(this, "WebView2 Runtime 不可用：请安装 Evergreen Runtime");
            throw new WebViewInitializationException("WebView2 初始化失败。", exception);
        }
    }

    public void Navigate(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (_core is null) throw new InvalidOperationException("WebView2 尚未初始化。");
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("云考页面必须使用 HTTPS。");
        }

        _core.Navigate(uri.AbsoluteUri);
    }

    public void Back()
    {
        if (_core?.CanGoBack == true) _core.GoBack();
    }

    public void Refresh() => _core?.Reload();

    public void OpenExternal()
    {
        if (CurrentUri is null) return;
        Process.Start(new ProcessStartInfo(CurrentUri.AbsoluteUri) { UseShellExecute = true });
    }

    public async Task<string> GetActiveQuestionHtmlAsync(CancellationToken cancellationToken = default)
    {
        EnsureAllowedPage();
        string script = "(() => { const x = document.querySelector('.swiper-slide-active, .practice_slide_content'); return x ? x.outerHTML : ''; })();";
        string result = await ExecuteScriptAsync(script, cancellationToken).ConfigureAwait(true);
        return DeserializeScriptString(result);
    }

    public async Task ClickNextAsync(CancellationToken cancellationToken = default)
    {
        EnsureAllowedPage();
        await ExecuteScriptAsync("window.YunKaoBridge && window.YunKaoBridge.next();", cancellationToken).ConfigureAwait(true);
    }

    public async Task FillCredentialsAsync(string schoolCode, string user, string password, CancellationToken cancellationToken = default)
    {
        EnsureAllowedPage();
        if (_core is null) throw new InvalidOperationException("WebView2 尚未初始化。");
        var message = new { type = "fillCredentials", schoolCode, user, password };
        _core.PostWebMessageAsJson(JsonSerializer.Serialize(message));
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
    }

    public async Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
    {
        EnsureAllowedPage();
        if (_core is null) throw new InvalidOperationException("WebView2 尚未初始化。");
        return await _core.ExecuteScriptAsync(script).AsTask(cancellationToken).ConfigureAwait(true);
    }

    public static bool IsAllowedHost(Uri? uri)
    {
        return uri is not null
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && AllowedHosts.Contains(uri.Host);
    }

    public static IReadOnlyCollection<string> AllowedHostNames => AllowedHosts;

    public async ValueTask DisposeAsync()
    {
        if (_core is not null)
        {
            _core.NavigationCompleted -= OnNavigationCompleted;
            _core.SourceChanged -= OnSourceChanged;
            _core.WebMessageReceived -= OnWebMessageReceived;
            _core.ProcessFailed -= OnProcessFailed;
        }

        await Task.CompletedTask;
    }

    private async void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        Uri? uri = TryGetUri(sender.Source);
        CurrentUri = uri;
        IsBridgeInstalled = false;
        if (args.IsSuccess && IsAllowedHost(uri) && !string.IsNullOrWhiteSpace(_bridgeScript))
        {
            try
            {
                await sender.ExecuteScriptAsync(_bridgeScript);
                IsBridgeInstalled = true;
                StatusChanged?.Invoke(this, "云考页面已安装安全桥接");
            }
            catch (Exception exception)
            {
                StatusChanged?.Invoke(this, $"Bridge 安装失败：{exception.Message}");
            }
        }
        else if (args.IsSuccess)
        {
            StatusChanged?.Invoke(this, "浏览器模式：当前页面不注入 Bridge");
        }

        NavigationChanged?.Invoke(
            this,
            new BrowserNavigationEventArgs(
                uri,
                args.IsSuccess,
                args.IsSuccess ? "导航完成" : $"导航失败：{args.WebErrorStatus}"));
    }

    private void OnSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        CurrentUri = TryGetUri(sender.Source);
        NavigationChanged?.Invoke(this, new BrowserNavigationEventArgs(CurrentUri, true, "地址已更新"));
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            BridgeMessageReceived?.Invoke(this, new BridgeMessageEventArgs(args.TryGetWebMessageAsString()));
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(this, $"Bridge 消息无效：{exception.Message}");
        }
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        IsBridgeInstalled = false;
        string message = $"WebView2 进程异常：{args.ProcessFailedKind}";
        ProcessFailed?.Invoke(this, message);
        StatusChanged?.Invoke(this, message);
    }

    private async Task<string> LoadBridgeScriptAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Scripts", "yunkao-bridge.js");
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "src", "YunKao.App", "Scripts", "yunkao-bridge.js");
        }

        return File.Exists(path)
            ? await File.ReadAllTextAsync(path).ConfigureAwait(true)
            : "";
    }

    private void EnsureAllowedPage()
    {
        if (!IsAllowedHost(CurrentUri))
        {
            throw new InvalidOperationException("当前页面不在云考 host 白名单内。");
        }
    }

    private static Uri? TryGetUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;
    }

    private static string DeserializeScriptString(string result)
    {
        if (string.IsNullOrWhiteSpace(result) || result == "null") return "";
        try { return JsonSerializer.Deserialize<string>(result) ?? ""; }
        catch { return result; }
    }
}

public sealed class WebViewInitializationException(string message, Exception innerException)
    : Exception(message, innerException);
