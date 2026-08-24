using YunKao.Core.Services;

namespace YunKao.Services;

/// <summary>
/// 应用级服务容器。页面不自行创建 Worker、配置和数据库连接，保证生命周期统一。
/// </summary>
public sealed class AppServices : IAsyncDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(45) };

    public AppServices()
    {
        Settings = new SettingsService();
        Diagnostics = new DiagnosticLog();
        History = new HistoryStore();
        Worker = new PythonWorkerClient();
        Exports = new ExportService(Worker);
        Ai = new AiService(_httpClient);
        AiQueue = new AiTaskQueue(Ai);
        Worker.LogReceived += (_, message) => Diagnostics.Warning(message);
        Diagnostics.EntryAdded += (_, entry) => _ = History.AddDiagnosticAsync(entry);
    }

    public SettingsService Settings { get; }
    public DiagnosticLog Diagnostics { get; }
    public HistoryStore History { get; }
    public PythonWorkerClient Worker { get; }
    public ExportService Exports { get; }
    public AiService Ai { get; }
    public AiTaskQueue AiQueue { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _ = Settings.Load();
        await History.InitializeAsync(cancellationToken).ConfigureAwait(false);
        Diagnostics.Info("应用服务已初始化");
    }

    public async ValueTask DisposeAsync()
    {
        await Exports.DisposeAsync().ConfigureAwait(false);
        await Worker.DisposeAsync().ConfigureAwait(false);
        await AiQueue.DisposeAsync().ConfigureAwait(false);
        await History.DisposeAsync().ConfigureAwait(false);
        _httpClient.Dispose();
    }
}
