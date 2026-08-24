using System.Text.Json;
using YunKao.Core.Models;
using YunKao.Core.Services;

namespace YunKao.Services;

public sealed record ExportRequest(
    string Format,
    string FilePath,
    IReadOnlyList<Question> Questions,
    bool IncludeAnswers = true,
    bool Watermark = true);

public sealed record ExportProgress(int Current, int Total, string Message);

public sealed record ExportResult(string Format, string FilePath, int QuestionCount);

/// <summary>
/// 导出边界：统一格式校验、Worker 进度转发和取消生命周期。
/// </summary>
public sealed class ExportService(PythonWorkerClient worker) : IAsyncDisposable
{
    private readonly PythonWorkerClient _worker = worker;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _exportGate = new(1, 1);
    private CancellationTokenSource? _activeCancellation;

    public event EventHandler<ExportProgress>? ProgressChanged;
    public bool IsExporting => _exportGate.CurrentCount == 0;

    public async Task<ExportResult> ExportAsync(
        ExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string format = NormalizeFormat(request.Format);
        if (request.Questions.Count == 0) throw new InvalidOperationException("没有可导出的题目。");
        if (string.IsNullOrWhiteSpace(request.FilePath)) throw new ArgumentException("导出路径不能为空。", nameof(request));

        if (!await _exportGate.WaitAsync(0).ConfigureAwait(false))
        {
            throw new ExportAlreadyRunningException();
        }

        CancellationTokenSource? linked = null;
        try
        {
            string? directory = Path.GetDirectoryName(request.FilePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_gate) _activeCancellation = linked;

            ProgressChanged?.Invoke(this, new ExportProgress(0, request.Questions.Count, "正在准备导出…"));
            JsonElement result = await _worker.CallAsync<JsonElement>(
                "export",
                new
                {
                    format,
                    filePath = request.FilePath,
                    questions = request.Questions,
                    includeAnswers = request.IncludeAnswers,
                    watermark = request.Watermark,
                },
                OnWorkerEvent,
                linked.Token).ConfigureAwait(false);

            string path = result.TryGetProperty("filePath", out JsonElement pathElement)
                ? pathElement.GetString() ?? request.FilePath
                : request.FilePath;
            int count = result.TryGetProperty("count", out JsonElement countElement)
                && countElement.TryGetInt32(out int resultCount)
                ? resultCount
                : request.Questions.Count;
            ProgressChanged?.Invoke(this, new ExportProgress(count, count, "导出完成"));
            return new ExportResult(format, path, count);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeCancellation, linked)) _activeCancellation = null;
            }
            linked?.Dispose();
            _exportGate.Release();
        }
    }

    public void CancelActive()
    {
        lock (_gate) _activeCancellation?.Cancel();
    }

    public ValueTask DisposeAsync()
    {
        CancelActive();
        _exportGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnWorkerEvent(WorkerEvent workerEvent)
    {
        if (!string.Equals(workerEvent.Event, "exportProgress", StringComparison.OrdinalIgnoreCase)) return;
        JsonElement data = workerEvent.Data;
        int current = ReadInt(data, "current");
        int total = ReadInt(data, "total");
        string message = data.TryGetProperty("message", out JsonElement messageElement)
            ? messageElement.GetString() ?? "正在导出…"
            : "正在导出…";
        ProgressChanged?.Invoke(this, new ExportProgress(current, total, message));
    }

    private static int ReadInt(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : 0;
    }

    private static string NormalizeFormat(string format)
    {
        string normalized = (format ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "markdown" => "md",
            "md" or "txt" or "docx" or "pdf" => normalized,
            _ => throw new ArgumentException($"不支持的导出格式：{format}", nameof(format)),
        };
    }
}

public sealed class ExportAlreadyRunningException()
    : InvalidOperationException("已有导出任务正在进行，请等待当前导出完成。");
