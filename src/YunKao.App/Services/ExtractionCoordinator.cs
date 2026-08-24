using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.UI.Xaml;
using YunKao.Controls;
using YunKao.Core.Models;
using YunKao.Core.Services;

namespace YunKao.Services;

/// <summary>
/// 将 WebView2 的 questionReady、Python Parser、ExtractionSession、AI 和导出串成一条可取消流程。
/// </summary>
public sealed class ExtractionCoordinator : IAsyncDisposable
{
    private readonly AppServices _services;
    private readonly BrowserShell _browser;
    private readonly ExtractionPanel _panel;
    private readonly ProgressCard _progress;
    private readonly AiStatusCard _aiStatus;
    private readonly EventList _events;
    private readonly SemaphoreSlim _questionGate = new(1, 1);
    private readonly ConcurrentBag<Task> _backgroundTasks = [];
    private readonly ExtractionSession _session = new();
    private CancellationTokenSource? _sessionCancellation;
    private Guid _sessionId;
    private bool _workerRestarted;
    private bool _disposed;

    public ExtractionCoordinator(
        AppServices services,
        BrowserShell browser,
        ExtractionPanel panel,
        ProgressCard progress,
        AiStatusCard aiStatus,
        EventList events)
    {
        _services = services;
        _browser = browser;
        _panel = panel;
        _progress = progress;
        _aiStatus = aiStatus;
        _events = events;
        _panel.StartRequested += (_, _) => Track(StartAsync());
        _panel.PauseRequested += (_, _) => TogglePause();
        _panel.StopRequested += (_, _) => Stop();
        _panel.ExportRequested += (_, format) => _ = ExportAsync(format);
        _browser.BridgeMessageReceived += OnBridgeMessageReceived;
        _session.Changed += OnSessionChanged;
        _services.Exports.ProgressChanged += OnExportProgress;
        if (_browser.Service.IsBridgeInstalled) _ = FillCredentialsOnBridgeReadyAsync();
    }

    public ExtractionSession Session => _session;

    public async Task StartAsync()
    {
        if (_session.Status is ExtractionStatus.Running or ExtractionStatus.Paused) return;
        _sessionCancellation?.Dispose();
        _sessionCancellation = new CancellationTokenSource();
        _sessionId = _session.Start();
        _workerRestarted = false;
        _panel.SetState("正在启动", running: true, paused: false);
        _events.Add("开始提取：准备 Worker");

        try
        {
            await _services.Worker.StartAsync(_sessionCancellation.Token);
            var health = await _services.Worker.CallAsync<JsonElement>("health", null, _sessionCancellation.Token);
            _events.Add($"Parser Worker 已就绪：{health.GetProperty("version").GetString()}");
            await ReadCurrentQuestionAsync(_sessionCancellation.Token);
        }
        catch (Exception exception)
        {
            if (_sessionCancellation?.IsCancellationRequested == true)
            {
                _session.Stop();
                return;
            }
            _services.Diagnostics.Error("提取启动失败", exception);
            _events.Add("提取启动失败：" + exception.Message, warning: true);
            _session.Fail();
        }
    }

    public void TogglePause()
    {
        if (_session.Status == ExtractionStatus.Running)
        {
            _session.Pause();
            _events.Add("已暂停：Bridge 事件会被忽略");
        }
        else if (_session.Status == ExtractionStatus.Paused)
        {
            _session.Resume();
            _events.Add("已继续：读取当前题");
            Track(ReadCurrentQuestionAsync(_sessionCancellation?.Token ?? CancellationToken.None));
        }
    }

    public void Stop()
    {
        _sessionCancellation?.Cancel();
        _services.Exports.CancelActive();
        _session.Stop();
        _events.Add("提取已停止");
        Track(PersistSessionAsync());
    }

    public async Task ExportAsync(string format)
    {
        if (_session.SavedCount == 0)
        {
            _events.Add("没有可导出的题目", warning: true);
            return;
        }

        if (format == "more") format = "md";
        AppSettings settings = _services.Settings.Load();
        string directory = string.IsNullOrWhiteSpace(settings.DefaultExportDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : settings.DefaultExportDirectory;
        Directory.CreateDirectory(directory);
        string extension = format == "docx" ? "docx" : format == "pdf" ? "pdf" : format == "txt" ? "txt" : "md";
        string path = Path.Combine(
            directory,
            $"{settings.ExportPrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}");
        try
        {
            _events.Add($"开始导出 {extension.ToUpperInvariant()}：{_session.SavedCount} 题");
            ExportResult result = await _services.Exports.ExportAsync(
                new ExportRequest(
                    format,
                    path,
                    _session.Questions,
                    IncludeAnswers: !settings.ExportWithoutAnswers,
                    Watermark: true),
                _sessionCancellation?.Token ?? CancellationToken.None);
            var record = new ExportRecord(
                0,
                extension.ToUpperInvariant(),
                result.FilePath,
                result.QuestionCount,
                DateTimeOffset.Now,
                "completed");
            await _services.History.SaveExportAsync(record);
            _events.Add($"导出完成：{result.FilePath}");
            if (settings.AutoOpenAfterExport)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.FilePath) { UseShellExecute = true });
            }
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
    }

    public async ValueTask DisposeAsync()
    {
        _sessionCancellation?.Cancel();
        _services.Exports.CancelActive();
        _disposed = true;
        _browser.BridgeMessageReceived -= OnBridgeMessageReceived;
        _session.Changed -= OnSessionChanged;
        _services.Exports.ProgressChanged -= OnExportProgress;
        try
        {
            await Task.WhenAll(_backgroundTasks.ToArray()).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch { }
        _questionGate.Dispose();
        await PersistSessionAsync().ConfigureAwait(false);
        _sessionCancellation?.Dispose();
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
                return;
            }
            if (messageType != "questionReady") return;
            if (_session.Status != ExtractionStatus.Running) return;
            Track(HandleQuestionReadyAsync(root.Clone(), _sessionCancellation?.Token ?? CancellationToken.None));
        }
        catch (Exception exception)
        {
            _services.Diagnostics.Warning("Bridge 消息解析失败：" + exception.Message);
        }
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
                SettingsService.SchoolCode,
                settings.YunKaoUser,
                password).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _services.Diagnostics.Warning("自动填写登录信息失败：" + exception.Message);
        }
    }

    private async Task HandleQuestionReadyAsync(JsonElement markerMessage, CancellationToken cancellationToken)
    {
        bool entered = false;
        try
        {
            entered = await _questionGate.WaitAsync(0, cancellationToken).ConfigureAwait(true);
            if (!entered) return;
            if (!_session.IsCurrent(_sessionId) || _session.Status != ExtractionStatus.Running) return;
            string marker = markerMessage.TryGetProperty("marker", out JsonElement markerElement)
                ? markerElement.GetString() ?? ""
                : "";
            if (!string.IsNullOrWhiteSpace(marker) && marker == _session.LastQuestionMarker) return;
            await ParseAndSaveCurrentQuestionAsync(markerMessage, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (entered) _questionGate.Release();
        }
    }

    private async Task ReadCurrentQuestionAsync(CancellationToken cancellationToken)
    {
        if (_session.Status != ExtractionStatus.Running) return;
        using JsonDocument document = JsonDocument.Parse("{\"type\":\"questionReady\",\"marker\":\"manual\"}");
        await HandleQuestionReadyAsync(document.RootElement.Clone(), cancellationToken).ConfigureAwait(true);
    }

    private async Task ParseAndSaveCurrentQuestionAsync(JsonElement markerMessage, CancellationToken cancellationToken)
    {
        string html = await _browser.Service.GetActiveQuestionHtmlAsync(cancellationToken);
        string baseUrl = _browser.Service.CurrentUri?.AbsoluteUri ?? "https://www.cctrcloud.net/";
        Question question = await CallWorkerWithRecoveryAsync<Question>(
            "parseQuestion",
            new { html, baseUrl },
            cancellationToken);
        if (string.IsNullOrWhiteSpace(question.Marker))
        {
            question.Marker = markerMessage.TryGetProperty("marker", out JsonElement marker)
                ? marker.GetString() ?? ""
                : "";
        }
        if (markerMessage.TryGetProperty("current", out JsonElement current)
            && int.TryParse(current.GetString(), out int currentNumber))
        {
            int total = markerMessage.TryGetProperty("total", out JsonElement totalElement)
                && int.TryParse(totalElement.GetString(), out int totalNumber)
                ? totalNumber
                : 0;
            _session.SetProgress(currentNumber, total, question.Marker);
        }

        bool added = _session.TryAddQuestion(_sessionId, question);
        if (!added)
        {
            _events.Add($"跳过重复题：{TrimForEvent(question.Title)}");
        }
        else
        {
            _events.Add($"已保存第 {_session.SavedCount} 题：{TrimForEvent(question.Title)}");
            if (string.IsNullOrWhiteSpace(question.Answer) && _services.Settings.Load().AiEnabled)
            {
                _session.IncrementAiPending();
                Track(FillAiAsync(_sessionId, question, cancellationToken));
            }
        }

        if (_session.Total > 0 && _session.Current >= _session.Total)
        {
            _session.Complete();
            return;
        }

        await _browser.Service.ClickNextAsync(cancellationToken);
    }

    private async Task FillAiAsync(Guid sessionId, Question question, CancellationToken parentToken)
    {
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
            AiResult result = await _services.AiQueue.EnqueueAsync(question, configuration, parentToken);
            _session.TryUpdateQuestion(sessionId, question, item =>
            {
                item.Answer = result.Answer;
                item.Analysis = result.Analysis;
                item.AnswerSource = "ai";
                item.AnalysisSource = "ai";
                item.AnswerConfidence = result.Confidence;
            });
            SetAiStatus("已补全", $"置信度 {result.Confidence:0.00}");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _services.Diagnostics.Warning("AI 补全失败：" + exception.Message);
            SetAiStatus("请求失败", "题目保留，稍后可重试");
        }
        finally
        {
            _session.DecrementAiPending();
        }
    }

    private async Task<T> CallWorkerWithRecoveryAsync<T>(string method, object parameters, CancellationToken cancellationToken)
    {
        try
        {
            return await _services.Worker.CallAsync<T>(method, parameters, cancellationToken);
        }
        catch when (!_workerRestarted && !cancellationToken.IsCancellationRequested)
        {
            _workerRestarted = true;
            _services.Diagnostics.Warning("Parser Worker 异常，尝试重启一次");
            await _services.Worker.StopAsync(CancellationToken.None);
            await _services.Worker.StartAsync(cancellationToken);
            return await _services.Worker.CallAsync<T>(method, parameters, cancellationToken);
        }
    }

    private void OnSessionChanged(object? sender, EventArgs args)
    {
        void Render()
        {
            bool running = _session.Status == ExtractionStatus.Running;
            bool paused = _session.Status == ExtractionStatus.Paused;
            _panel.SetState(_session.Status switch
            {
                ExtractionStatus.Running => "提取中",
                ExtractionStatus.Paused => "已暂停",
                ExtractionStatus.Completing => "等待 AI 完成",
                ExtractionStatus.Completed => "已完成",
                ExtractionStatus.Error => "异常",
                _ => "等待开始",
            }, running, paused);
            _progress.SetProgress(_session.Current, _session.Total, $"已保存 {_session.SavedCount} 题 · AI 待处理 {_session.AiPending}");
        }

        if (!_panel.DispatcherQueue.TryEnqueue(Render)) Render();
    }

    private void OnExportProgress(object? sender, ExportProgress progress)
    {
        void Render()
        {
            _progress.SetProgress(progress.Current, progress.Total, progress.Message);
            _events.Add(progress.Message);
        }

        if (!_progress.DispatcherQueue.TryEnqueue(Render)) Render();
        if (_session.Status == ExtractionStatus.Completed) Track(PersistSessionAsync());
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
        try { await _services.History.SaveSessionAsync(_session, ""); } catch { }
    }

    private static string TrimForEvent(string value) => value.Length <= 30 ? value : value[..30] + "…";
}
