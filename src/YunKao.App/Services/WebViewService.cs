using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using YunKao.Core.Services;

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

public sealed class BrowserHttpEventArgs(Uri? uri, int statusCode, BrowserResourceKind resourceKind) : EventArgs
{
    public Uri? Uri { get; } = uri;
    public int StatusCode { get; } = statusCode;
    public BrowserResourceKind ResourceKind { get; } = resourceKind;
}

/// <summary>
/// WebView2 生命周期、安全域名和 JS Bridge 的边界。
/// </summary>
public sealed class WebViewService : IAsyncDisposable
{
    private WebView2? _view;
    private CoreWebView2? _core;
    private string? _bridgeScript;
    private int _blankProbeAttempts;
    private readonly SemaphoreSlim _recoveryGate = new(1, 1);
    private readonly BrowserRecoveryStateMachine _recoveryState = new();
    private RecoveryWaiters? _recoveryWaiters;

    public bool IsInitialized => _core is not null;
    public bool IsBridgeInstalled { get; private set; }
    public bool IsRecovering => _recoveryState.IsRecovering;
    public Uri? CurrentUri { get; private set; }
    public BrowserPageState PageState { get; private set; } = new(false, false);
    public string BrowserVersion => _core?.Environment.BrowserVersionString ?? "未初始化";

    public event EventHandler<BrowserNavigationEventArgs>? NavigationChanged;
    public event EventHandler<BridgeMessageEventArgs>? BridgeMessageReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ProcessFailed;
    public event EventHandler<BrowserHttpEventArgs>? HttpStatusChanged;
    public event EventHandler? RecoveryCompleted;
    public event EventHandler<string>? RecoveryFailed;

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
            // WinUI 3 的 WebView2 WinRT 投影只暴露无参 CreateAsync；其默认 UserDataFolder
            // 由 WebView2 按应用包隔离，足以保持登录 Cookie，路径变量保留用于兼容旧实现。
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync().AsTask(cancellationToken);
            await view.EnsureCoreWebView2Async(environment).AsTask(cancellationToken);
            _core = view.CoreWebView2;
            _core.NavigationCompleted += OnNavigationCompleted;
            _core.SourceChanged += OnSourceChanged;
            _core.WebMessageReceived += OnWebMessageReceived;
            _core.ProcessFailed += OnProcessFailed;
            _core.WebResourceResponseReceived += OnWebResourceResponseReceived;
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

    public bool CanGoBack => _core?.CanGoBack == true;
    public bool CanGoForward => _core?.CanGoForward == true;

    public void Back()
    {
        if (_core?.CanGoBack == true) _core.GoBack();
    }

    public void Forward()
    {
        if (_core?.CanGoForward == true) _core.GoForward();
    }

    public void GoHome()
    {
        Navigate(new Uri("https://www.cctrcloud.net/"));
    }

    public void Refresh() => _core?.Reload();

    public void OpenExternal()
    {
        if (CurrentUri is null) return;
        Process.Start(new ProcessStartInfo(CurrentUri.AbsoluteUri) { UseShellExecute = true });
    }

    public async Task<bool> IsPracticePageAsync(CancellationToken cancellationToken = default)
    {
        BrowserPageState state = await GetPageStateAsync(cancellationToken).ConfigureAwait(true);
        return state.IsPracticeReady;
    }

    public async Task<string> ProbePageStateAsync(CancellationToken cancellationToken = default)
    {
        if (_core is null) return "Initializing";
        BrowserPageState state = await GetPageStateAsync(cancellationToken).ConfigureAwait(true);
        return state.IsLogin ? "LoginRequired" : state.IsPracticeReady ? "PracticeReady" : "Browsing";
    }

    public async Task<BrowserPageState> GetPageStateAsync(CancellationToken cancellationToken = default)
    {
        if (_core is null)
        {
            return new(false, false, CurrentUri?.AbsoluteUri ?? "", "");
        }

        try
        {
            const string script = "(() => { const state = window.YunKaoBridge && window.YunKaoBridge.checkPracticeState ? window.YunKaoBridge.checkPracticeState() : { isLogin: false, isPractice: false, url: window.location.href, title: document.title }; return JSON.stringify(state); })();";
            string result = await ExecuteScriptAsync(script, cancellationToken).ConfigureAwait(true);
            BrowserPageState state = DeserializePageState(DeserializeScriptString(result));
            PageState = state;
            return state;
        }
        catch
        {
            PageState = new(
                IsLogin: false,
                IsPractice: false,
                Url: CurrentUri?.AbsoluteUri ?? PageState.Url,
                Title: PageState.Title);
            return PageState;
        }
    }

    public async Task<string> GetActiveQuestionHtmlAsync(CancellationToken cancellationToken = default)
    {
        EnsureAllowedPage();
        const string script = "window.YunKaoBridge && window.YunKaoBridge.getActiveQuestionHtml ? window.YunKaoBridge.getActiveQuestionHtml() : '';";
        string result = await ExecuteScriptAsync(script, cancellationToken).ConfigureAwait(true);
        return DeserializeScriptString(result);
    }

    public async Task<bool> ClickNextAsync(CancellationToken cancellationToken = default)
    {
        EnsureAllowedPage();
        string result = await ExecuteScriptAsync("window.YunKaoBridge && window.YunKaoBridge.next();", cancellationToken).ConfigureAwait(true);
        return DeserializeScriptBoolean(result);
    }

    public async Task<string> ReadQuestionMarkerAsync(CancellationToken cancellationToken = default)
    {
        EnsureAllowedPage();
        const string script = "window.YunKaoBridge && window.YunKaoBridge.readMarkerValue ? window.YunKaoBridge.readMarkerValue() : '';";
        string result = await ExecuteScriptAsync(script, cancellationToken).ConfigureAwait(true);
        return DeserializeScriptString(result);
    }

    public async Task<string> GetCookieHeaderAsync(Uri? uri = null, CancellationToken cancellationToken = default)
    {
        if (_core is null) return "";
        Uri target = uri ?? CurrentUri ?? new Uri("https://www.cctrcloud.net/");
        IReadOnlyList<CoreWebView2Cookie> cookies = await _core.CookieManager
            .GetCookiesAsync(target.AbsoluteUri).AsTask(cancellationToken).ConfigureAwait(true);
        return string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
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
        return CloudResourceClassifier.IsAllowedHost(uri);
    }

    public static IReadOnlyCollection<string> AllowedHostNames => CloudResourceClassifier.AllowedHostNames;

    public static bool IsAllowedBridgePage(Uri? uri)
    {
        if (!IsAllowedHost(uri)) return false;
        return CloudResourceClassifier.Classify(uri, "") is not BrowserResourceKind.Other;
    }

    public async Task<bool> RecoverAsync(CancellationToken cancellationToken = default)
    {
        if (!await _recoveryGate.WaitAsync(0, cancellationToken).ConfigureAwait(true)) return false;
        bool completed = false;
        try
        {
            if (_view is null)
            {
                throw new InvalidOperationException("WebView2 控件尚未创建。");
            }

            if (!_recoveryState.Begin()) return false;
            Uri targetUri = CurrentUri ?? new Uri("https://www.cctrcloud.net/");
            DetachCoreEvents();
            _core = null;
            IsBridgeInstalled = false;
            var waiters = new RecoveryWaiters();
            _recoveryWaiters = waiters;

            await InitializeAsync(_view, cancellationToken).ConfigureAwait(true);
            Navigate(targetUri);
            bool navigationSucceeded = await waiters.Navigation.Task
                .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                .ConfigureAwait(true);
            if (!navigationSucceeded)
            {
                throw new InvalidOperationException("恢复后的页面导航失败。");
            }

            if (IsAllowedBridgePage(targetUri))
            {
                await waiters.Bridge.Task
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(true);
                await waiters.PageState.Task
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(true);
            }
            else
            {
                // 非云考页面不注入 Bridge，但仍可视为浏览器本体恢复完成；继续提取由上层门禁阻止。
                _recoveryState.MarkBridgeReady();
                _recoveryState.MarkPageState(false);
            }

            if (_recoveryState.Phase != BrowserRecoveryPhase.Completed)
            {
                throw new InvalidOperationException("恢复后的页面状态尚未就绪。");
            }

            completed = true;
            RecoveryCompleted?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _recoveryState.Fail();
            StatusChanged?.Invoke(this, "WebView2 重建失败：" + exception.Message);
            RecoveryFailed?.Invoke(this, exception.Message);
            return false;
        }
        finally
        {
            _recoveryWaiters = null;
            if (!completed && _recoveryState.IsRecovering) _recoveryState.Fail();
            _recoveryGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_core is not null)
        {
            _core.NavigationCompleted -= OnNavigationCompleted;
            _core.SourceChanged -= OnSourceChanged;
            _core.WebMessageReceived -= OnWebMessageReceived;
            _core.ProcessFailed -= OnProcessFailed;
            _core.WebResourceResponseReceived -= OnWebResourceResponseReceived;
        }

        _recoveryState.Fail();
        _recoveryGate.Dispose();
        await Task.CompletedTask;
    }

    private async void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        Uri? uri = TryGetUri(sender.Source);
        CurrentUri = uri;
        IsBridgeInstalled = false;
        PageState = new(false, false, uri?.AbsoluteUri ?? "", "");
        if (args.IsSuccess && IsAllowedBridgePage(uri) && !string.IsNullOrWhiteSpace(_bridgeScript))
        {
            try
            {
                await sender.ExecuteScriptAsync(_bridgeScript);
                IsBridgeInstalled = true;
                _blankProbeAttempts = 0;
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

        if (_recoveryState.IsRecovering)
        {
            _recoveryState.MarkNavigationCompleted(args.IsSuccess);
            _recoveryWaiters?.Navigation.TrySetResult(args.IsSuccess);
        }

        NavigationChanged?.Invoke(
            this,
            new BrowserNavigationEventArgs(
                uri,
                args.IsSuccess,
                args.IsSuccess ? "导航完成" : $"导航失败：{args.WebErrorStatus}"));

        if (args.IsSuccess) _ = ProbeBlankPageAsync(sender, uri);
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
            string message = args.TryGetWebMessageAsString();
            ObserveBridgeLifecycle(message);
            BridgeMessageReceived?.Invoke(this, new BridgeMessageEventArgs(message));
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(this, $"Bridge 消息无效：{exception.Message}");
        }
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        if (IsRecovering) return;
        IsBridgeInstalled = false;
        string message = $"WebView2 进程异常：{args.ProcessFailedKind}";
        ProcessFailed?.Invoke(this, message);
        StatusChanged?.Invoke(this, message);
        _ = RecoverAsync();
    }

    private void OnWebResourceResponseReceived(CoreWebView2 sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (args.Response is null) return;
        int statusCode = args.Response.StatusCode;
        if (!CloudResourceClassifier.ShouldReportStatus(statusCode)) return;
        Uri? uri = TryGetUri(args.Request.Uri);
        string resourceContext = IsCurrentDocumentUri(uri) ? "Document" : "";
        BrowserResourceKind resourceKind = CloudResourceClassifier.Classify(uri, resourceContext);
        if (resourceKind == BrowserResourceKind.Other) return;

        HttpStatusChanged?.Invoke(this, new BrowserHttpEventArgs(uri, statusCode, resourceKind));
        string message = statusCode switch
        {
            401 => "登录状态已失效，提取已暂停。",
            403 => "当前页面无访问权限。",
            408 => "云考请求超时，正在等待服务恢复。",
            429 => "请求过于频繁，正在等待服务恢复。",
            >= 500 => "云考服务暂时不可用，正在等待恢复。",
            _ => $"页面请求失败：HTTP {statusCode}",
        };
        StatusChanged?.Invoke(this, message);
    }

    private async Task ProbeBlankPageAsync(CoreWebView2 sender, Uri? uri)
    {
        if (!IsAllowedHost(uri)) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1.2)).ConfigureAwait(true);
            if (!Equals(CurrentUri, uri)) return;
            string result = await sender.ExecuteScriptAsync(
                "JSON.stringify({ ready: document.readyState, body: (document.body?.innerText || '').trim().length, title: document.title, bridge: !!window.__yunkaoBridgeInstalled })");
            using JsonDocument document = JsonDocument.Parse(DeserializeScriptString(result));
            JsonElement root = document.RootElement;
            bool blank = root.GetProperty("ready").GetString() == "complete"
                && root.GetProperty("body").GetInt32() <= 1
                && !root.GetProperty("bridge").GetBoolean();
            if (!blank) return;
            if (_blankProbeAttempts++ == 0)
            {
                StatusChanged?.Invoke(this, "页面内容为空，正在自动重新加载…");
                sender.Reload();
            }
            else
            {
                StatusChanged?.Invoke(this, "页面仍为空白，可重新加载或在外部浏览器打开。");
            }
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(this, "页面探针失败：" + exception.Message);
        }
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

    private void ObserveBridgeLifecycle(string message)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            JsonElement root = document.RootElement;
            string messageType = root.TryGetProperty("type", out JsonElement type)
                ? type.GetString() ?? ""
                : "";
            if (messageType.Equals("bridgeReady", StringComparison.Ordinal))
            {
                _recoveryState.MarkBridgeReady();
                _recoveryWaiters?.Bridge.TrySetResult(true);
                return;
            }

            if (!messageType.Equals("pageState", StringComparison.Ordinal)) return;
            BrowserPageState state = ParsePageState(root);
            PageState = state;
            _recoveryState.MarkPageState(state.IsPracticeReady);
            _recoveryWaiters?.PageState.TrySetResult(true);
        }
        catch (JsonException)
        {
            // 原始消息仍会交给 Coordinator 记录诊断，这里只忽略无法参与恢复判定的消息。
        }
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

    private bool IsCurrentDocumentUri(Uri? uri)
    {
        return uri is not null
            && CurrentUri is not null
            && Uri.Compare(uri, CurrentUri, UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
                UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static BrowserPageState DeserializePageState(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(false, false);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return ParsePageState(document.RootElement);
        }
        catch (JsonException)
        {
            return new(false, false);
        }
    }

    private static BrowserPageState ParsePageState(JsonElement root)
    {
        bool isLogin = ReadBoolean(root, "isLogin");
        bool isPractice = ReadBoolean(root, "isPractice");
        string url = root.TryGetProperty("url", out JsonElement urlElement)
            ? urlElement.GetString() ?? ""
            : "";
        string title = root.TryGetProperty("title", out JsonElement titleElement)
            ? titleElement.GetString() ?? ""
            : "";
        return new BrowserPageState(isLogin, isPractice, url, title);
    }

    private static bool ReadBoolean(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out bool parsed) && parsed,
            _ => false,
        };
    }

    private static string DeserializeScriptString(string result)
    {
        if (string.IsNullOrWhiteSpace(result) || result == "null") return "";
        try { return JsonSerializer.Deserialize<string>(result) ?? ""; }
        catch { return result; }
    }

    private static bool DeserializeScriptBoolean(string result)
    {
        return bool.TryParse(result.Trim(), out bool value) && value;
    }

    private sealed class RecoveryWaiters
    {
        public TaskCompletionSource<bool> Navigation { get; } = CreateSource();
        public TaskCompletionSource<bool> Bridge { get; } = CreateSource();
        public TaskCompletionSource<bool> PageState { get; } = CreateSource();

        private static TaskCompletionSource<bool> CreateSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void DetachCoreEvents()
    {
        if (_core is null) return;
        _core.NavigationCompleted -= OnNavigationCompleted;
        _core.SourceChanged -= OnSourceChanged;
        _core.WebMessageReceived -= OnWebMessageReceived;
        _core.ProcessFailed -= OnProcessFailed;
        _core.WebResourceResponseReceived -= OnWebResourceResponseReceived;
    }
}

public sealed class WebViewInitializationException(string message, Exception innerException)
    : Exception(message, innerException);
