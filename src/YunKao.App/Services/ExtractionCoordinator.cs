using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.UI.Xaml;
using YunKao.Controls;
using YunKao.Core.Models;
using YunKao.Core.Services;

namespace YunKao.Services;

public sealed record WorkspaceBannerInfo(
    string Title,
    string Message,
    Microsoft.UI.Xaml.Controls.InfoBarSeverity Severity,
    string? ActionLabel,
    Action? ActionCallback);

/// <summary>
/// 工作台提取控制器。Bridge 事件进入单消费者队列，页面生命周期不再决定业务会话生命周期。
/// </summary>
public sealed class ExtractionCoordinator : IAsyncDisposable
{
    private readonly AppServices _services;
    private readonly BrowserShell _browser;
    private readonly ExtractionPanel _panel;
    private readonly ProgressCard _progress;
    private readonly ExportCard _export;
    private readonly AiStatusCard _aiStatus;
    private readonly EventList _events;
    private readonly ExtractionSession _session;
    private readonly ImageResolver _imageResolver;
    private readonly Channel<QuestionMarker> _markerQueue = Channel.CreateUnbounded<QuestionMarker>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly ConcurrentBag<Task> _backgroundTasks = [];
    private readonly Task _markerLoop;
    private CancellationTokenSource? _extractionCancellation;
    private CancellationTokenSource? _aiCancellation;
    private Guid _sessionId;
    private string _lastProcessedMarker = "";
    private bool _restoredSessionPending;
    private bool _workerRestarted;
    private bool _disposed;

    public event EventHandler<WorkspaceBannerInfo>? BannerRequested;

    public ExtractionCoordinator(
        AppServices services,
        BrowserShell browser,
        ExtractionPanel panel,
        ProgressCard progress,
        ExportCard export,
        AiStatusCard aiStatus,
        EventList events)
    {
        _services = services;
        _browser = browser;
        _panel = panel;
        _progress = progress;
        _export = export;
        _aiStatus = aiStatus;
        _events = events;
        _session = services.Workspace.Session;
        _services.RegisterWorkspaceCoordinator(this);
        _imageResolver = new ImageResolver(browser.Service);
        _markerLoop = ProcessMarkerQueueAsync(_lifetimeCancellation.Token);

        _panel.StartRequested += (_, _) => Track(StartAsync());
        _panel.PauseRequested += (_, _) => TogglePause();
        _panel.StopRequested += (_, _) => Stop();
        _panel.ClearRequested += (_, _) => ClearCurrentSession();
        _panel.RestartRequested += (_, _) => RestartCurrentSession();
        _panel.RestoreRequested += (_, _) => RestoreInterruptedSession();
        _export.ExportRequested += (_, format) => Track(ExportAsync(format));
        _export.PracticeModeChanged += OnPracticeModeChanged;
        _browser.BridgeMessageReceived += OnBridgeMessageReceived;
        _browser.ProcessFailed += OnBrowserProcessFailed;
        _browser.Service.RecoveryCompleted += OnBrowserRecoveryCompleted;
        _browser.Service.RecoveryFailed += OnBrowserRecoveryFailed;
        _browser.Service.HttpStatusChanged += OnHttpStatusChanged;
        _browser.Service.NavigationChanged += OnBrowserNavigationChanged;
        _session.Changed += OnSessionChanged;
        _services.Exports.ProgressChanged += OnExportProgress;
        _services.Initialized += OnServicesInitialized;
        if (_browser.Service.IsBridgeInstalled) Track(FillCredentialsOnBridgeReadyAsync());
        _export.SetPracticeMode(_services.Settings.Load().ExportWithoutAnswers);
        RenderInterruptedPrompt();
    }

    private void OnBrowserNavigationChanged(object? sender, BrowserNavigationEventArgs args)
    {
        Track(UpdatePageReadinessAsync());
    }

    private async Task UpdatePageReadinessAsync()
    {
        try
        {
            bool ready = await _browser.Service.IsPracticePageAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            if (_session.Status is not (ExtractionStatus.Running or ExtractionStatus.Paused))
            {
                _panel.DispatcherQueue.TryEnqueue(() =>
                {
                    _panel.SetState(_session.Status, _session.SavedCount, ready);
                });
            }
        }
        catch { }
    }

    public ExtractionSession Session => _session;

    public async Task StartAsync()
    {
        await _startGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_session.Status == ExtractionStatus.Running
                || (_session.Status == ExtractionStatus.Paused && !_restoredSessionPending)) return;

            bool isPractice = await _browser.Service.IsPracticePageAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            if (!isPractice)
            {
                _events.Add("当前页面未检测到可解析的练习题，请先进入练习/题库页面", warning: true);
                BannerRequested?.Invoke(this, new WorkspaceBannerInfo(
                    "请先进入练习页面",
                    "当前页面未检测到考试/练习题。请在左侧浏览器导航至具体练习或刷题页面后，再点击开始提取。",
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                    "前往首页",
                    () => _browser.Service.GoHome()));
                return;
            }

            _extractionCancellation?.Dispose();
            _aiCancellation?.Dispose();
            _extractionCancellation = new CancellationTokenSource();
            _aiCancellation = new CancellationTokenSource();
            if (_restoredSessionPending && _session.ResumeRestored())
            {
                _restoredSessionPending = false;
                _sessionId = _session.SessionId;
            }
            else
            {
                _sessionId = _session.Start(sourceUrl: _browser.Service.CurrentUri?.AbsoluteUri ?? "");
            }
            _lastProcessedMarker = "";
            _workerRestarted = false;
            _services.Workspace.AuthExpired = false;
            _services.Workspace.PermissionDenied = false;
            _panel.SetState(ExtractionStatus.Running, _session.SavedCount, true);
            _events.Add("开始提取：准备 Worker");

            try
            {
                await _services.Worker.StartAsync(_extractionCancellation.Token).ConfigureAwait(true);
                JsonElement health = await _services.Worker.CallAsync<JsonElement>(
                    "health", null, _extractionCancellation.Token).ConfigureAwait(true);
                string version = health.TryGetProperty("version", out JsonElement versionElement)
                    ? versionElement.GetString() ?? "unknown"
                    : "unknown";
                _events.Add($"Parser Worker 已就绪：{version}");
                QueueCurrentQuestion();
            }
            catch (OperationCanceledException) when (_extractionCancellation.IsCancellationRequested)
            {
                _session.Stop();
            }
            catch (Exception exception)
            {
                _services.Diagnostics.Error("提取启动失败", exception);
                _events.Add("提取启动失败：" + exception.Message, warning: true);
                _session.Fail();
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <summary>
    /// 安全恢复历史会话：如果当前正在运行/已提取题目，在获得确认后先停止并保存当前任务，再载入快照并保持暂停。
    /// </summary>
    public async Task<bool> RestoreHistoricalSessionAsync(ExtractionSessionSnapshot snapshot, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _startGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_session.Status is ExtractionStatus.Running or ExtractionStatus.Paused or ExtractionStatus.Completing
                || _session.SavedCount > 0)
            {
                if (!force) return false;
                _extractionCancellation?.Cancel();
                _aiCancellation?.Cancel();
                await PersistSessionAsync().ConfigureAwait(true);
                _session.Stop();
            }

            if (!_session.Restore(snapshot)) return false;

            _restoredSessionPending = true;
            _sessionId = _session.SessionId;
            _lastProcessedMarker = snapshot.LastQuestionMarker;
            _services.Workspace.CurrentCourse = snapshot.Course;
            _panel.ClearInterrupted();
            _events.Add($"已恢复历史任务：{snapshot.Questions.Count} 题；当前保持暂停，请确认页面后再开始");

            bool ready = await _browser.Service.IsPracticePageAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            _panel.DispatcherQueue.TryEnqueue(() =>
            {
                _panel.SetState(ExtractionStatus.Paused, _session.SavedCount, ready);
            });

            if (!string.IsNullOrWhiteSpace(snapshot.SourceUrl))
            {
                BannerRequested?.Invoke(this, new WorkspaceBannerInfo(
                    "历史任务已恢复",
                    $"已成功载入 {snapshot.Questions.Count} 道历史题目。请确认左侧题库页面，点击继续提取即可续抓。",
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
                    "打开原题库",
                    () =>
                    {
                        if (Uri.TryCreate(snapshot.SourceUrl, UriKind.Absolute, out Uri? uri))
                        {
                            _browser.Service.Navigate(uri);
                        }
                    }));
            }

            return true;
        }
        finally
        {
            _startGate.Release();
        }
    }

    public void TogglePause()
    {
        if (_session.Status == ExtractionStatus.Running)
        {
            _session.Pause();
            _events.Add("已暂停：不会自动推进下一题");
        }
        else if (_session.Status == ExtractionStatus.Paused)
        {
            if (_restoredSessionPending)
            {
                Track(StartAsync());
                return;
            }
            _session.Resume();
            _events.Add("已继续：读取当前题");
            QueueCurrentQuestion();
        }
    }

    public void Stop()
    {
        if (_session.Status is not (ExtractionStatus.Running or ExtractionStatus.Paused)) return;
        _extractionCancellation?.Cancel();
        _aiCancellation?.Cancel();
        // 停止提取不取消导出；已保存题目必须仍可导出。
        _session.Stop();
        _events.Add("提取已停止，已保存题目仍可导出");
        Track(PersistSessionAsync());
    }

    /// <summary>
    /// 清空当前题目并取消其提取/AI 生命周期；正在导出的快照不受影响。
    /// </summary>
    public void ClearCurrentSession()
    {
        string clearedSessionId = _session.SessionId == Guid.Empty ? "" : _session.SessionId.ToString("N");
        _extractionCancellation?.Cancel();
        _aiCancellation?.Cancel();
        _restoredSessionPending = false;
        _lastProcessedMarker = "";
        _session.Clear();
        if (!string.IsNullOrWhiteSpace(clearedSessionId)) Track(_services.History.DeleteSessionAsync(clearedSessionId));
        _events.Add("已清空本次题目，可重新开始提取");
    }

    public void RestartCurrentSession()
    {
        ClearCurrentSession();
        Track(StartAsync());
    }

    public async Task ExportAsync(string format)
    {
        IReadOnlyList<Question> snapshot = _session.Questions;
        if (snapshot.Count == 0)
        {
            _events.Add("没有可导出的题目", warning: true);
            return;
        }

        AppSettings settings = _services.Settings.Load();
        string directory = string.IsNullOrWhiteSpace(settings.DefaultExportDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : settings.DefaultExportDirectory;
        string extension = ExportPathGenerator.NormalizeExtension(format);
        string path = ExportPathGenerator.CreateUniquePath(directory, settings.ExportPrefix, format);

        try
        {
            _export.SetExporting(true);
            _events.Add($"开始导出 {extension.ToUpperInvariant()}：{snapshot.Count} 题");
            var resolved = new List<Question>(snapshot.Count);
            foreach (Question question in snapshot)
            {
                resolved.Add(await _imageResolver.ResolveAsync(question).ConfigureAwait(true));
            }

            ExportResult result = await _services.Exports.ExportAsync(
                new ExportRequest(
                    format,
                    path,
                    resolved,
                    IncludeAnswers: !settings.ExportWithoutAnswers,
                    Watermark: true),
                CancellationToken.None).ConfigureAwait(true);
            var record = new ExportRecord(
                0,
                extension.ToUpperInvariant(),
                result.FilePath,
                result.QuestionCount,
                DateTimeOffset.Now,
                "completed",
                _session.SessionId.ToString("N"),
                !settings.ExportWithoutAnswers);
            await _services.History.SaveExportAsync(record).ConfigureAwait(true);
            _events.Add($"导出完成：{result.FilePath}");
            if (settings.AutoOpenAfterExport)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.FilePath) { UseShellExecute = true });
            }
        }
        catch (ExportAlreadyRunningException exception)
        {
            _events.Add(exception.Message, warning: true);
        }
        catch (OperationCanceledException)
        {
            _events.Add("导出已取消", warning: true);
        }
        catch (Exception exception)
        {
            _services.Diagnostics.Error("导出失败", exception);
            _events.Add("导出失败：" + exception.Message, warning: true);
        }
        finally
        {
            _export.SetExporting(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _extractionCancellation?.Cancel();
        _aiCancellation?.Cancel();
        _services.Exports.CancelActive();
        _lifetimeCancellation.Cancel();
        _markerQueue.Writer.TryComplete();
        _browser.BridgeMessageReceived -= OnBridgeMessageReceived;
        _browser.ProcessFailed -= OnBrowserProcessFailed;
        _browser.Service.RecoveryCompleted -= OnBrowserRecoveryCompleted;
        _browser.Service.RecoveryFailed -= OnBrowserRecoveryFailed;
        _browser.Service.HttpStatusChanged -= OnHttpStatusChanged;
        _session.Changed -= OnSessionChanged;
        _services.Exports.ProgressChanged -= OnExportProgress;
        _export.PracticeModeChanged -= OnPracticeModeChanged;
        _services.Initialized -= OnServicesInitialized;
        try
        {
            await _markerLoop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            await Task.WhenAll(_backgroundTasks.ToArray()).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch { }
        await PersistSessionAsync().ConfigureAwait(false);
        _imageResolver.Dispose();
        _extractionCancellation?.Dispose();
        _aiCancellation?.Dispose();
        _lifetimeCancellation.Dispose();
        _startGate.Dispose();
    }

    private void OnBridgeMessageReceived(object? sender, BridgeMessageEventArgs args)
    {
        if (_disposed) return;
        try
        {
            using JsonDocument document = JsonDocument.Parse(args.Message);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("type", out JsonElement type)) return;
            string? messageType = type.GetString();
            if (messageType == "bridgeReady")
            {
                Track(FillCredentialsOnBridgeReadyAsync());
                Track(UpdatePageReadinessAsync());
                return;
            }
            if (messageType == "pageState")
            {
                bool isPractice = root.TryGetProperty("isPractice", out JsonElement pElem) && pElem.GetBoolean();
                _panel.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_session.Status is not (ExtractionStatus.Running or ExtractionStatus.Paused))
                    {
                        _panel.SetState(_session.Status, _session.SavedCount, isPractice);
                    }
                });
                return;
            }
            if (messageType != "questionReady" || _session.Status != ExtractionStatus.Running) return;
            _markerQueue.Writer.TryWrite(new QuestionMarker(_sessionId, root.Clone(), 0));
        }
        catch (Exception exception)
        {
            _services.Diagnostics.Warning("Bridge 消息解析失败：" + exception.Message);
        }
    }

    private void OnHttpStatusChanged(object? sender, BrowserHttpEventArgs args)
    {
        if (args.StatusCode is not (401 or 403 or 429 or 500 or 502 or 503)) return;
        if (args.StatusCode == 401)
        {
            _services.Workspace.AuthExpired = true;
            if (_session.Status is ExtractionStatus.Running or ExtractionStatus.Paused)
            {
                _session.Pause();
                _events.Add("登录状态已失效，提取已暂停；已保存题目不会丢失", warning: true);
            }
            BannerRequested?.Invoke(this, new WorkspaceBannerInfo(
                "登录已失效",
                $"登录状态已过期，提取已暂停；已保存的 {_session.SavedCount} 道题目不会丢失。",
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                "重新登录",
                () => _browser.Service.Navigate(new Uri("https://www.cctrcloud.net/"))));
        }
        else if (args.StatusCode == 403)
        {
            _services.Workspace.PermissionDenied = true;
            if (_session.Status is ExtractionStatus.Running or ExtractionStatus.Paused)
            {
                _session.Pause();
                _events.Add("当前页面无访问权限，提取已暂停", warning: true);
            }
            BannerRequested?.Invoke(this, new WorkspaceBannerInfo(
                "无访问权限",
                "当前账号没有访问此题库的权限，提取已暂停。",
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
                "返回首页",
                () => _browser.Service.GoHome()));
        }
        else if (args.StatusCode is 500 or 502 or 503)
        {
            if (_session.Status is ExtractionStatus.Running or ExtractionStatus.Paused)
            {
                _session.Pause();
                _events.Add($"云考服务暂时不可用 (HTTP {args.StatusCode})，提取已暂停", warning: true);
            }
            BannerRequested?.Invoke(this, new WorkspaceBannerInfo(
                "服务暂时不可用",
                $"云考服务器返回 HTTP {args.StatusCode}，提取已暂停；请检查网络或稍后重试。",
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
                "刷新页面",
                () => _browser.Service.Refresh()));
        }
    }

    private void OnBrowserProcessFailed(object? sender, string message)
    {
        if (_session.Status is ExtractionStatus.Running or ExtractionStatus.Paused)
        {
            _session.Pause();
            _events.Add("WebView2 进程异常退出，正在自动恢复…", warning: true);
        }
        _services.Diagnostics.Warning(message);
        BannerRequested?.Invoke(this, new WorkspaceBannerInfo(
            "页面组件异常退出",
            $"页面组件异常退出，正在自动恢复；已提取的 {_session.SavedCount} 道题目已安全保存。",
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
            null,
            null));
    }

    private void OnBrowserRecoveryCompleted(object? sender, EventArgs args)
    {
        _events.Add("WebView2 自动恢复完成", warning: false);
        Track(UpdatePageReadinessAsync());
        BannerRequested?.Invoke(this, new WorkspaceBannerInfo(
            "页面已恢复",
            $"页面组件已自动恢复；已保存 {_session.SavedCount} 道题目。确认题目页面稳定后可点击继续提取。",
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
            "继续提取",
            () => TogglePause()));
    }

    private void OnBrowserRecoveryFailed(object? sender, string message)
    {
        _events.Add("WebView2 自动恢复失败：" + message, warning: true);
        BannerRequested?.Invoke(this, new WorkspaceBannerInfo(
            "页面恢复失败",
            "页面组件自动恢复失败，请尝试刷新页面或重新启动应用。",
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
            "刷新页面",
            () => _browser.Service.Refresh()));
    }

    private void OnServicesInitialized(object? sender, EventArgs args) => RenderInterruptedPrompt();

    private void RenderInterruptedPrompt()
    {
        ExtractionSessionSnapshot? snapshot = _services.InterruptedSessions.FirstOrDefault();
        if (snapshot is null || _session.SavedCount > 0) return;

        void Render()
        {
            _panel.SetInterrupted(snapshot.Questions.Count);
            _events.Add($"发现上次未完成任务：已保存 {snapshot.Questions.Count} 题，可恢复为当前题库");
        }

        if (!_panel.DispatcherQueue.TryEnqueue(Render)) Render();
    }

    private void RestoreInterruptedSession()
    {
        ExtractionSessionSnapshot? snapshot = _services.InterruptedSessions.FirstOrDefault();
        if (snapshot is null || _session.SavedCount > 0) return;
        if (!_session.Restore(snapshot)) return;
        _restoredSessionPending = true;
        _services.Workspace.CurrentCourse = snapshot.Course;
        _panel.ClearInterrupted();
        _events.Add($"已恢复上次任务：{snapshot.Questions.Count} 题；当前保持暂停，请确认页面后再开始");
    }

    private async Task FillCredentialsOnBridgeReadyAsync()
    {
        try
        {
            AppSettings settings = _services.Settings.Load();
            if (string.IsNullOrWhiteSpace(settings.YunKaoUser)) return;
            string password = settings.RememberYunKaoPassword
                ? _services.Settings.GetYunKaoPassword(settings.YunKaoUser)
                : "";
            await _browser.Service.FillCredentialsAsync(
                SettingsService.SchoolCode, settings.YunKaoUser, password).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _services.Diagnostics.Warning("自动填写登录信息失败：" + exception.Message);
        }
    }

    private void QueueCurrentQuestion()
    {
        if (_session.Status != ExtractionStatus.Running || _sessionId == Guid.Empty) return;
        using JsonDocument document = JsonDocument.Parse("{\"type\":\"questionReady\",\"marker\":\"manual\"}");
        _markerQueue.Writer.TryWrite(new QuestionMarker(_sessionId, document.RootElement.Clone(), 0));
    }

    private async Task ProcessMarkerQueueAsync(CancellationToken lifetimeToken)
    {
        try
        {
            await foreach (QuestionMarker item in _markerQueue.Reader.ReadAllAsync(lifetimeToken).ConfigureAwait(false))
            {
                if (!_session.IsCurrent(item.SessionId) || _session.Status != ExtractionStatus.Running) continue;
                try
                {
                    await HandleQuestionReadyAsync(item, lifetimeToken).ConfigureAwait(true);
                }
                catch (QuestionNotReadyException) when (item.Attempt < 10 && !lifetimeToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), lifetimeToken).ConfigureAwait(true);
                    _markerQueue.Writer.TryWrite(item with { Attempt = item.Attempt + 1 });
                }
                catch (WorkerCallException exception) when (exception.Code == "question_not_ready" && item.Attempt < 10 && !lifetimeToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), lifetimeToken).ConfigureAwait(true);
                    _markerQueue.Writer.TryWrite(item with { Attempt = item.Attempt + 1 });
                }
                catch (WorkerCallException exception) when (exception.Code is "question_unsupported" or "question_not_ready")
                {
                    _events.Add("当前页面还没有可解析题目，已暂停等待页面稳定", warning: true);
                    if (_session.Status == ExtractionStatus.Running)
                    {
                        _session.Pause();
                        BannerRequested?.Invoke(this, new WorkspaceBannerInfo(
                            "题目解析暂停",
                            "当前页面尚未检测到可解析的题目内容，已自动暂停；请确认页面题目已加载完全。",
                            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                            "重新提取",
                            () => QueueCurrentQuestion()));
                    }
                }
                catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException) when (_extractionCancellation?.IsCancellationRequested == true)
                {
                }
                catch (Exception exception)
                {
                    _services.Diagnostics.Error("题目处理失败", exception);
                    _events.Add("题目处理失败：" + exception.Message, warning: true);
                    if (_session.Status == ExtractionStatus.Running)
                    {
                        _session.Fail();
                        Track(PersistSessionAsync());
                    }
                }
            }
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleQuestionReadyAsync(QuestionMarker marker, CancellationToken lifetimeToken)
    {
        CancellationToken extractionToken = _extractionCancellation?.Token ?? lifetimeToken;
        string markerValue = ReadString(marker.Message, "marker");
        if (!string.IsNullOrWhiteSpace(markerValue)
            && (markerValue == _lastProcessedMarker || markerValue == _session.LastQuestionMarker)) return;
        await ParseAndSaveCurrentQuestionAsync(marker, extractionToken).ConfigureAwait(true);
    }

    private async Task ParseAndSaveCurrentQuestionAsync(QuestionMarker marker, CancellationToken cancellationToken)
    {
        string html = await _browser.Service.GetActiveQuestionHtmlAsync(cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(html)) throw new QuestionNotReadyException();
        string baseUrl = _browser.Service.CurrentUri?.AbsoluteUri ?? "https://www.cctrcloud.net/";
        Question question = await CallWorkerWithRecoveryAsync<Question>(
            "parseQuestion", new { html, baseUrl }, cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(question.Marker)) question.Marker = ReadString(marker.Message, "marker");
        question = await _imageResolver.ResolveAsync(question, cancellationToken).ConfigureAwait(true);

        if (TryReadProgress(marker.Message, out int current, out int total))
        {
            _session.TrySetProgress(marker.SessionId, current, total, question.Marker);
        }

        bool added = _session.TryAddQuestion(marker.SessionId, question);
        if (!_session.IsCurrent(marker.SessionId)) return;
        _lastProcessedMarker = question.Marker;
        if (!added)
        {
            _events.Add($"跳过重复题：{TrimForEvent(question.Title)}");
            Track(PersistSessionAsync());
        }
        else
        {
            _events.Add($"已保存第 {_session.SavedCount} 题：{TrimForEvent(question.Title)}");
            Track(PersistSessionAsync());
            if (string.IsNullOrWhiteSpace(question.Answer) && _services.Settings.Load().AiEnabled)
            {
                if (_session.IncrementAiPending(marker.SessionId))
                {
                    Track(FillAiAsync(marker.SessionId, question, _aiCancellation?.Token ?? CancellationToken.None));
                }
            }
        }

        if (_session.Total > 0 && _session.Current >= _session.Total)
        {
            _session.Complete();
            Track(PersistSessionAsync());
            return;
        }

        bool clicked = await _browser.Service.ClickNextAsync(cancellationToken).ConfigureAwait(true);
        if (!clicked)
        {
            _events.Add("未找到下一题按钮，提取已暂停，请检查页面结构", warning: true);
            _session.Pause();
            return;
        }

        await WaitForNextMarkerAsync(marker.SessionId, question.Marker, cancellationToken).ConfigureAwait(true);
    }

    private async Task WaitForNextMarkerAsync(Guid sessionId, string previousMarker, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(true);
        if (!_session.IsCurrent(sessionId)) return;
        string marker = await _browser.Service.ReadQuestionMarkerAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(marker) && !string.Equals(marker, previousMarker, StringComparison.Ordinal))
        {
            _markerQueue.Writer.TryWrite(new QuestionMarker(sessionId, CreateMarkerJson(marker), 0));
            return;
        }

        if (_session.IsCurrent(sessionId) && _session.Status == ExtractionStatus.Running)
        {
            _events.Add("等待下一题超时，提取已暂停，请检查网络或页面", warning: true);
            _session.Pause();
        }
    }

    private async Task FillAiAsync(Guid sessionId, Question question, CancellationToken parentToken)
    {
        bool succeeded = false;
        try
        {
            AppSettings settings = _services.Settings.Load();
            var configuration = new AiRequestConfiguration
            {
                Provider = settings.AiProvider,
                BaseUrl = settings.AiBaseUrl,
                Model = settings.AiModel,
                SupportsImages = settings.AiSupportsImages,
                ApiKey = _services.Settings.GetAiKey(settings.AiProvider),
            };
            AiResult result = await _services.AiQueue.EnqueueAsync(question, configuration, parentToken).ConfigureAwait(false);
            _session.TryUpdateQuestion(sessionId, question, item =>
            {
                item.Answer = result.Answer;
                item.Analysis = result.Analysis;
                item.AnswerSource = "ai";
                item.AnalysisSource = "ai";
                item.AnswerConfidence = result.Confidence;
            });
            succeeded = true;
            Track(PersistSessionAsync());
            SetAiStatus("已补全", $"置信度 {result.Confidence:0.00}");
        }
        catch (OperationCanceledException) { }
        catch (AiHttpException exception)
        {
            _services.Diagnostics.Warning($"AI 请求失败 HTTP {exception.StatusCode}：{exception.Message}");
            SetAiStatus("请求失败", exception.StatusCode == 429 ? "请求过于频繁，稍后重试" : "题目保留，稍后可重试");
        }
        catch (Exception exception)
        {
            _services.Diagnostics.Warning("AI 补全失败：" + exception.Message);
            SetAiStatus("请求失败", "题目保留，稍后可重试");
        }
        finally
        {
            if (_session.CompleteAiTask(sessionId, succeeded)) Track(PersistSessionAsync());
        }
    }

    private async Task<T> CallWorkerWithRecoveryAsync<T>(string method, object parameters, CancellationToken cancellationToken)
    {
        try
        {
            return await _services.Worker.CallAsync<T>(method, parameters, cancellationToken).ConfigureAwait(true);
        }
        catch (WorkerCallException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (!_workerRestarted && !cancellationToken.IsCancellationRequested)
        {
            _workerRestarted = true;
            _services.Diagnostics.Warning("Parser Worker 传输异常，尝试重启一次");
            await _services.Worker.StopAsync(CancellationToken.None).ConfigureAwait(true);
            await _services.Worker.StartAsync(cancellationToken).ConfigureAwait(true);
            return await _services.Worker.CallAsync<T>(method, parameters, cancellationToken).ConfigureAwait(true);
        }
    }

    private void OnSessionChanged(object? sender, EventArgs args)
    {
        void Render()
        {
            App.MainWindow?.SetTaskState(_session.Status);
            _panel.SetState(_session.Status, _session.SavedCount);
            _progress.SetProgress(_session.Current, _session.Total,
                $"已保存 {_session.SavedCount} 题 · AI 待处理 {_session.AiPending}", _session.SavedCount, _session.AiPending);
        }

        if (!_panel.DispatcherQueue.TryEnqueue(Render)) Render();
    }

    private void OnExportProgress(object? sender, ExportProgress progress)
    {
        void Render()
        {
            _export.SetExportProgress(progress.Current, progress.Total, progress.Message);
            _events.Add(progress.Message);
        }

        if (!_progress.DispatcherQueue.TryEnqueue(Render)) Render();
    }

    private void OnPracticeModeChanged(object? sender, bool enabled)
    {
        try
        {
            AppSettings settings = _services.Settings.Load();
            settings.ExportWithoutAnswers = enabled;
            _services.Settings.Save(settings);
            _events.Add(enabled ? "已切换为练习版导出" : "已切换为答案版导出");
        }
        catch (Exception exception)
        {
            _services.Diagnostics.Error("保存练习版设置失败", exception);
        }
    }

    private void Track(Task task)
    {
        if (_disposed) return;
        _backgroundTasks.Add(task);
        _ = ObserveAsync(task);
    }

    private void SetAiStatus(string status, string detail)
    {
        void Render() => _aiStatus.SetStatus(status, detail);
        if (!_aiStatus.DispatcherQueue.TryEnqueue(Render)) Render();
    }

    private async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _services.Diagnostics.Error("后台任务失败", exception); }
    }

    private async Task PersistSessionAsync()
    {
        if (_session.SessionId == Guid.Empty || _session.SavedCount == 0) return;
        try { await _services.History.SaveSessionAsync(_session, _services.Workspace.CurrentCourse).ConfigureAwait(false); }
        catch (Exception exception) { _services.Diagnostics.Warning("历史会话保存失败：" + exception.Message); }
    }

    private static bool TryReadProgress(JsonElement root, out int current, out int total)
    {
        current = ReadInt(root, "current");
        total = ReadInt(root, "total");
        return current > 0;
    }

    private static JsonElement CreateMarkerJson(string marker)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new { type = "questionReady", marker }));
        return document.RootElement.Clone();
    }

    private static string ReadString(JsonElement root, string property)
        => root.TryGetProperty(property, out JsonElement value) ? value.GetString() ?? "" : "";

    private static int ReadInt(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
            ? number
            : int.TryParse(value.GetString(), out number) ? number : 0;
    }

    private static string TrimForEvent(string value) => value.Length <= 30 ? value : value[..30] + "…";

    private sealed record QuestionMarker(Guid SessionId, JsonElement Message, int Attempt);

    private sealed class QuestionNotReadyException() : Exception("active question is not ready");
}
