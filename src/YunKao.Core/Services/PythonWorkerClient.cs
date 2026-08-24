using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using YunKao.Core.Models;

namespace YunKao.Core.Services;

public interface IWorkerClient
{
    bool IsRunning { get; }
    event EventHandler<WorkerEvent>? EventReceived;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<T> CallAsync<T>(string method, object? parameters, CancellationToken cancellationToken = default);
    Task CancelAsync(string requestId, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 管理长期驻留的 Python Worker。应用 stdout 不参与协议，Worker stderr 只进入诊断日志。
/// </summary>
public sealed class PythonWorkerClient : IWorkerClient, IAsyncDisposable
{
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, Action<WorkerEvent>> _eventHandlers = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private Process? _process;
    private Stream? _input;
    private Stream? _output;
    private CancellationTokenSource? _lifetime;
    private Task? _readLoop;
    private Task? _stderrLoop;

    public bool IsRunning => _process is { HasExited: false };

    public event EventHandler<WorkerEvent>? EventReceived;
    public event EventHandler<string>? LogReceived;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning) return;

            ProcessStartInfo startInfo = WorkerLocator.CreateStartInfo();
            Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Exited += OnProcessExited;
            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 YunKao.Worker。");
            }

            _process = process;
            _input = process.StandardInput.BaseStream;
            _output = process.StandardOutput.BaseStream;
            _lifetime = new CancellationTokenSource();
            _readLoop = Task.Run(() => ReadLoopAsync(_lifetime.Token), CancellationToken.None);
            _stderrLoop = Task.Run(() => ReadStderrAsync(process.StandardError, _lifetime.Token), CancellationToken.None);
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task<T> CallAsync<T>(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        return await CallAsync<T>(method, parameters, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 发送请求并将属于该 requestId 的 Worker 事件交给调用方，例如导出进度。
    /// </summary>
    public async Task<T> CallAsync<T>(
        string method,
        object? parameters,
        Action<WorkerEvent>? eventHandler,
        CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        if (_input is null || !IsRunning)
        {
            throw new InvalidOperationException("YunKao.Worker 尚未运行。");
        }

        string id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        if (eventHandler is not null) _eventHandlers[id] = eventHandler;
        try
        {
            var request = new
            {
                protocol = 1,
                id,
                method,
                @params = parameters ?? new { },
            };

            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WorkerProtocol.WriteAsync(_input, request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }

            JsonElement response = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool ok = response.TryGetProperty("ok", out JsonElement okElement) && okElement.GetBoolean();
            if (!ok)
            {
                WorkerError? error = response.TryGetProperty("error", out JsonElement errorElement)
                    ? errorElement.Deserialize<WorkerError>(_jsonOptions)
                    : null;
                if (cancellationToken.IsCancellationRequested
                    && string.Equals(error?.Code, "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                throw new WorkerCallException(error?.Code ?? "worker_error", error?.Message ?? "Worker 请求失败");
            }

            if (!response.TryGetProperty("result", out JsonElement resultElement))
            {
                return default!;
            }

            return resultElement.Deserialize<T>(_jsonOptions)!;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // UI 取消等待后，仍通知 Worker 停止实际任务，避免导出继续占用资源。
            _ = CancelAsync(id);
            throw;
        }
        finally
        {
            _pending.TryRemove(id, out _);
            _eventHandlers.TryRemove(id, out _);
        }
    }

    public async Task CancelAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId) || !IsRunning) return;
        try
        {
            await CallAsync<JsonElement>(
                "cancel",
                new { targetId = requestId },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogReceived?.Invoke(this, $"Worker 取消请求失败：{exception.Message}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process = _process;
        if (process is null) return;

        try
        {
            if (!process.HasExited)
            {
                await CallAsync<object>("shutdown", null, cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogReceived?.Invoke(this, $"Worker 停止时异常：{exception.Message}");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }

            CompletePending(new InvalidOperationException("YunKao.Worker 已停止。"));
            _lifetime?.Cancel();
            process.Dispose();
            _process = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch { }

        _startGate.Dispose();
        _writeGate.Dispose();
        _lifetime?.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_output is null) return;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using JsonDocument document = await WorkerProtocol.ReadAsync(_output, cancellationToken).ConfigureAwait(false);
                JsonElement root = document.RootElement.Clone();
                if (root.TryGetProperty("type", out JsonElement type) && type.GetString() == "event")
                {
                    WorkerEvent? workerEvent = root.Deserialize<WorkerEvent>(_jsonOptions);
                    if (workerEvent is not null)
                    {
                        EventReceived?.Invoke(this, workerEvent);
                        if (workerEvent.Data.ValueKind == JsonValueKind.Object
                            && workerEvent.Data.TryGetProperty("requestId", out JsonElement requestIdElement))
                        {
                            string requestId = requestIdElement.GetString() ?? "";
                            if (_eventHandlers.TryGetValue(requestId, out Action<WorkerEvent>? handler))
                            {
                                try { handler(workerEvent); } catch (Exception exception)
                                {
                                    LogReceived?.Invoke(this, $"Worker 进度处理失败：{exception.Message}");
                                }
                            }
                        }
                    }
                    continue;
                }

                if (root.TryGetProperty("id", out JsonElement id))
                {
                    string requestId = id.GetString() ?? "";
                    if (_pending.TryRemove(requestId, out TaskCompletionSource<JsonElement>? completion))
                    {
                        completion.TrySetResult(root);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            CompletePending(exception);
            LogReceived?.Invoke(this, $"Worker 读取失败：{exception.Message}");
        }
    }

    private async Task ReadStderrAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                if (!string.IsNullOrWhiteSpace(line)) LogReceived?.Invoke(this, line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        CompletePending(new InvalidOperationException("YunKao.Worker 意外退出。"));
        LogReceived?.Invoke(this, $"Worker 已退出，ExitCode={_process?.ExitCode}");
    }

    private void CompletePending(Exception exception)
    {
        _eventHandlers.Clear();
        foreach (KeyValuePair<string, TaskCompletionSource<JsonElement>> item in _pending.ToArray())
        {
            if (_pending.TryRemove(item.Key, out TaskCompletionSource<JsonElement>? completion))
            {
                completion.TrySetException(exception);
            }
        }
    }
}

public sealed class WorkerCallException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class WorkerLocator
{
    public static ProcessStartInfo CreateStartInfo()
    {
        string? explicitWorker = Environment.GetEnvironmentVariable("YUNKAO_WORKER_EXE");
        if (!string.IsNullOrWhiteSpace(explicitWorker) && File.Exists(explicitWorker))
        {
            return CreateProcessInfo(explicitWorker, [], Path.GetDirectoryName(explicitWorker));
        }

        string? root = FindFile("worker/YunKao.Worker.exe");
        if (root is not null) return CreateProcessInfo(root, [], Path.GetDirectoryName(root));

        string? onedir = FindFile("worker/YunKao.Worker/YunKao.Worker.exe");
        if (onedir is not null) return CreateProcessInfo(onedir, [], Path.GetDirectoryName(onedir));

        string? script = FindFile("worker/worker_main.py");
        if (script is null)
        {
            throw new FileNotFoundException("找不到 worker/worker_main.py 或 YunKao.Worker.exe。");
        }

        string python = Environment.GetEnvironmentVariable("YUNKAO_PYTHON") ?? "python";
        string workingDirectory = Directory.GetParent(Path.GetDirectoryName(script)!)?.FullName
            ?? Directory.GetCurrentDirectory();
        return CreateProcessInfo(python, [script], workingDirectory);
    }

    private static ProcessStartInfo CreateProcessInfo(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    private static string? FindFile(string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? current = new(start);
            for (int depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            {
                string candidate = Path.Combine(current.FullName, normalized);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
