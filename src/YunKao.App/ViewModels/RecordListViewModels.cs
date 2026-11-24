using System.Collections.ObjectModel;
using YunKao.Core.Models;
using YunKao.Core.Presentation;
using YunKao.Core.Services;

namespace YunKao.ViewModels;

/// <summary>分页列表页的公共状态：加载中、空状态、状态文字、是否还有更多。</summary>
public abstract class PagedListViewModel<TRow> : ObservableObject
{
    private bool _isLoading;
    private bool _canLoadMore;
    private string _statusText = "";
    private string _emptyTitle = "";
    private string _emptyText = "";
    private bool _hasLoaded;

    public ObservableCollection<TRow> Items { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        protected set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsIdle => !_isLoading;
    public bool CanLoadMore { get => _canLoadMore; protected set => SetProperty(ref _canLoadMore, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string EmptyTitle { get => _emptyTitle; protected set => SetProperty(ref _emptyTitle, value); }
    public string EmptyText { get => _emptyText; protected set => SetProperty(ref _emptyText, value); }
    public bool IsEmpty => _hasLoaded && IsIdle && Items.Count == 0;

    protected void FinishLoad()
    {
        _hasLoaded = true;
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public abstract class SearchableRecordsViewModel<TRow> : PagedListViewModel<TRow> where TRow : class
{
    protected const int PageSize = 50;
    private CancellationTokenSource? _pending;
    private string _query = "";
    private bool _readFailed;
    private string _resultLabel = "";

    protected abstract string DefaultEmptyTitle { get; }
    protected abstract string DefaultEmptyText { get; }
    protected abstract Task<IReadOnlyList<TRow>> ReadAsync(TRow? last, string query, CancellationToken token);
    public string ResultLabel { get => _resultLabel; private set => SetProperty(ref _resultLabel, value); }
    public string EmptyAction => _readFailed ? "重新读取" : _query.Length > 0 ? "清除搜索" : "前往工作台";
    public bool ReadFailed => _readFailed;

    public Task SearchAsync(string text)
    {
        string query = text.Trim();
        if (query == _query) return Task.CompletedTask;
        _query = query;
        CanLoadMore = false;
        Items.Clear();
        ResultLabel = "";
        return LoadAsync(reset: true, debounce: query.Length > 0);
    }

    public async Task LoadAsync(bool reset, bool debounce = false)
    {
        if (!reset && (IsLoading || !CanLoadMore)) return;
        _pending?.Cancel();
        using var request = new CancellationTokenSource();
        _pending = request;
        IsLoading = true;
        StatusText = "";
        string query = _query;
        TRow? last = reset ? null : Items.LastOrDefault();
        try
        {
            if (debounce) await Task.Delay(180, request.Token);
            // SQLite reads and file existence checks may block, particularly for network drives.
            var rows = await Task.Run(() => ReadAsync(last, query, request.Token), request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (reset) Items.Clear();
            foreach (var row in rows.Take(PageSize)) Items.Add(row);
            CanLoadMore = rows.Count > PageSize;
            _readFailed = false;
            ResultLabel = Items.Count == 0 ? "" : $"{(CanLoadMore ? "已显示" : "共")} {Items.Count} 条{(query.Length > 0 ? "匹配记录" : "记录")}";
            EmptyTitle = query.Length > 0 ? "没有找到匹配的记录" : DefaultEmptyTitle;
            EmptyText = query.Length > 0 ? "换个关键词，或清除搜索查看全部记录。" : DefaultEmptyText;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (request.IsCancellationRequested) return;
            _readFailed = true;
            EmptyTitle = "暂时无法读取记录";
            EmptyText = exception.Message;
            StatusText = "读取失败，请刷新重试。";
            App.Services.Diagnostics.Error("读取记录列表失败", exception);
        }
        finally
        {
            if (ReferenceEquals(_pending, request))
            {
                _pending = null;
                IsLoading = false;
                FinishLoad();
                OnPropertyChanged(nameof(EmptyAction));
            }
        }
    }
}

public sealed class SessionRow(ExtractionSessionRecord record)
{
    public ExtractionSessionRecord Record { get; } = record;
    public string Title { get; } = string.IsNullOrWhiteSpace(record.Course) ? "练习题库" : record.Course;
    public string Meta { get; } = $"{record.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {RecordStatusText.ForSession(record.Status)}";
    public string Counts { get; } = $"{record.QuestionCount} 题"
        + (record.DuplicateCount > 0 ? $" · 重复 {record.DuplicateCount}" : "")
        + (record.ErrorCount > 0 ? $" · 异常 {record.ErrorCount}" : "")
        + (record.AiCount > 0 ? $" · AI {record.AiCount} 题" : "");
    public string Url { get; } = record.SourceUrl ?? "";
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
    public bool CanRestore => Record.QuestionCount > 0;
}

public sealed class HistoryViewModel : SearchableRecordsViewModel<SessionRow>
{
    protected override string DefaultEmptyTitle => "还没有提取记录";
    protected override string DefaultEmptyText => "在工作台打开练习并开始提取。保存后的题库可以在这里恢复，继续提取或导出。";

    protected override async Task<IReadOnlyList<SessionRow>> ReadAsync(SessionRow? last, string query, CancellationToken token)
    {
        var rows = await App.Services.History.GetSessionsAsync(last?.Record.StartedAt, PageSize + 1, token, query, last?.Record.Id);
        return rows.Select(row => new SessionRow(row)).ToArray();
    }
}

public sealed class ExportRow(ExportRecord record)
{
    public ExportRecord Record { get; } = record;
    public bool FileExists { get; } = File.Exists(record.FilePath);
    public string FileName { get; } = string.IsNullOrWhiteSpace(Path.GetFileName(record.FilePath))
        ? record.FilePath
        : Path.GetFileName(record.FilePath);
    public string FilePath => Record.FilePath;
    public string Format => (Record.Format ?? "").ToUpperInvariant();
    public string Meta => $"{Record.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {Record.QuestionCount} 题 · "
        + $"{(Record.IncludeAnswers ? "答案版" : "练习版")} · {RecordStatusText.ForExport(Record.Status)}"
        + (FileExists ? "" : " · 文件已移动或删除");
    public bool CanReExport => !string.IsNullOrWhiteSpace(Record.SessionId);
    public bool ShowReExport => !FileExists && CanReExport;
}

public sealed class ExportsViewModel : SearchableRecordsViewModel<ExportRow>
{
    protected override string DefaultEmptyTitle => "还没有导出文件";
    protected override string DefaultEmptyText => "在工作台导出第一份题库后，就可以在这里打开文件或找到它所在的文件夹。";

    protected override async Task<IReadOnlyList<ExportRow>> ReadAsync(ExportRow? last, string query, CancellationToken token)
    {
        var rows = await App.Services.History.GetExportsAsync(last?.Record.CreatedAt, PageSize + 1, token, query, last?.Record.Id);
        return rows.Select(row =>
        {
            token.ThrowIfCancellationRequested();
            return new ExportRow(row);
        }).ToArray();
    }
}

public sealed record ReportItem(string Label, string Value);

public sealed class DiagnosticRow(DiagnosticRecord record)
{
    public string Time { get; } = record.CreatedAt.LocalDateTime.ToString("MM-dd HH:mm:ss");
    public string Level { get; } = (record.Level ?? "").Trim().ToLowerInvariant() switch
    {
        "error" => "错误",
        "warning" => "警告",
        "info" => "信息",
        var other => other,
    };
    public string Message { get; } = record.Message;
    public bool IsWarning { get; } = record.Level is "warning" or "error";
}

public sealed class DiagnosticsViewModel : PagedListViewModel<DiagnosticRow>
{
    private const int PageSize = 200;
    private DateTimeOffset? _cursor;
    private string _report = "";

    /// <summary>纯文本摘要（用于复制、导出）；同时拆成键值行供界面以表格展示。</summary>
    public string Report
    {
        get => _report;
        set
        {
            if (!SetProperty(ref _report, value)) return;
            ReportItems.Clear();
            foreach (string line in (value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int split = line.IndexOf('：');
                ReportItems.Add(split > 0
                    ? new ReportItem(line[..split].Trim(), line[(split + 1)..].Trim())
                    : new ReportItem("", line.Trim()));
            }
        }
    }

    public ObservableCollection<ReportItem> ReportItems { get; } = [];

    public DiagnosticsViewModel()
    {
        TaskState.Activity.CollectionChanged += OnActivityChanged;
    }

    /// <summary>页面卸载时解除对应用级集合的订阅，避免页面实例被长期持有。</summary>
    public void Detach() => TaskState.Activity.CollectionChanged -= OnActivityChanged;

    private void OnActivityChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
        => OnPropertyChanged(nameof(HasActivity));

    public TaskViewModel TaskState => App.Services.Task;
    public bool HasActivity => TaskState.Activity.Count > 0;

    public async Task LoadAsync(bool reset)
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            if (reset)
            {
                _cursor = null;
                Items.Clear();
                EmptyTitle = "暂无诊断事件";
                EmptyText = "运行中出现的警告与错误会记录在这里。";
            }
            IReadOnlyList<DiagnosticRecord> rows = await App.Services.History.GetDiagnosticsAsync(_cursor, PageSize);
            foreach (DiagnosticRecord row in rows) Items.Add(new DiagnosticRow(row));
            if (rows.Count > 0) _cursor = rows[^1].CreatedAt;
            CanLoadMore = rows.Count == PageSize;
        }
        catch (Exception exception)
        {
            EmptyTitle = "诊断记录读取失败";
            EmptyText = exception.Message;
            CanLoadMore = false;
        }
        finally
        {
            IsLoading = false;
            FinishLoad();
        }
    }
}
