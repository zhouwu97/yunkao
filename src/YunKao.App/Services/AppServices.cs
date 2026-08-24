using YunKao.Core.Models;
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
        Diagnostics.EntryAdded += (_, entry) => _ = History.QueueDiagnosticAsync(entry);
    }

    public SettingsService Settings { get; }
    public DiagnosticLog Diagnostics { get; }
    public HistoryStore History { get; }
    public PythonWorkerClient Worker { get; }
    public ExportService Exports { get; }
    public AiService Ai { get; }
    public AiTaskQueue AiQueue { get; }
    public WorkspaceRuntime Workspace { get; } = new();
    public IReadOnlyList<ExtractionSessionSnapshot> InterruptedSessions { get; private set; } = Array.Empty<ExtractionSessionSnapshot>();
    public event EventHandler? Initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = Settings.Load();
        MotionService.SetReduceMotion(settings.ReduceMotion || MotionService.ReduceMotion);
        await History.InitializeAsync(cancellationToken).ConfigureAwait(false);
        InterruptedSessions = await History.GetInterruptedSessionsAsync(cancellationToken).ConfigureAwait(false);
        Diagnostics.Info("应用服务已初始化");
        Initialized?.Invoke(this, EventArgs.Empty);
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
