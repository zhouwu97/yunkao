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
            if (SetProperty(ref _isLoading, value)) OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_isLoading;
    public bool CanLoadMore { get => _canLoadMore; protected set => SetProperty(ref _canLoadMore, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string EmptyTitle { get => _emptyTitle; protected set => SetProperty(ref _emptyTitle, value); }
    public string EmptyText { get => _emptyText; protected set => SetProperty(ref _emptyText, value); }
    public bool IsEmpty => _hasLoaded && Items.Count == 0;

    protected void FinishLoad()
    {
        _hasLoaded = true;
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed class SessionRow(ExtractionSessionRecord record)
{
    public ExtractionSessionRecord Record { get; } = record;
    public string Title { get; } = string.IsNullOrWhiteSpace(record.Course) ? "练习题库" : record.Course;
    public string Meta { get; } = $"{record.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {RecordStatusText.ForSession(record.Status)}";
    public string Counts { get; } = $"{record.QuestionCount} 题 · 重复 {record.DuplicateCount} · 异常 {record.ErrorCount} · AI {record.AiCount} 题";
    public string Url { get; } = record.SourceUrl ?? "";
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
    public bool CanRestore => Record.QuestionCount > 0;
}

public sealed class HistoryViewModel : PagedListViewModel<SessionRow>
{
    private const int PageSize = 50;
    private DateTimeOffset? _cursor;

    public async Task LoadAsync(bool reset)
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "正在读取记录…";
        try
        {
            if (reset)
            {
                _cursor = null;
                Items.Clear();
                EmptyTitle = "还没有提取记录";
                EmptyText = "在工作台打开练习页，开始提取。保存的题目会出现在这里，之后可以恢复或导出。";
            }
            IReadOnlyList<ExtractionSessionRecord> rows = await App.Services.History.GetSessionsAsync(_cursor, PageSize);
            foreach (ExtractionSessionRecord row in rows) Items.Add(new SessionRow(row));
            if (rows.Count > 0) _cursor = rows[^1].StartedAt;
            CanLoadMore = rows.Count == PageSize;
            StatusText = Items.Count > 0 ? $"已显示 {Items.Count} 条记录" : "";
        }
        catch (Exception exception)
        {
            EmptyTitle = "暂时无法读取记录";
            EmptyText = "历史记录读取失败：" + exception.Message;
            StatusText = "读取失败，请点击刷新重试。";
            CanLoadMore = false;
            App.Services.Diagnostics.Error("历史记录读取失败", exception);
        }
        finally
        {
            IsLoading = false;
            FinishLoad();
        }
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
}

public sealed class ExportsViewModel : PagedListViewModel<ExportRow>
{
    private const int PageSize = 50;
    private DateTimeOffset? _cursor;

    public async Task LoadAsync(bool reset)
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "正在读取记录…";
        try
        {
            if (reset)
            {
                _cursor = null;
                Items.Clear();
                EmptyTitle = "还没有导出文件";
                EmptyText = "提取题目后，在左侧任务卡选择导出格式。生成的文件会记录在这里，方便再次打开。";
            }
            IReadOnlyList<ExportRecord> rows = await App.Services.History.GetExportsAsync(_cursor, PageSize);
            // 文件存在性检查放到后台，避免网络盘阻塞界面。
            ExportRow[] mapped = await Task.Run(() => rows.Select(row => new ExportRow(row)).ToArray());
            foreach (ExportRow row in mapped) Items.Add(row);
            if (rows.Count > 0) _cursor = rows[^1].CreatedAt;
            CanLoadMore = rows.Count == PageSize;
            StatusText = Items.Count > 0 ? $"已显示 {Items.Count} 条记录" : "";
        }
        catch (Exception exception)
        {
            EmptyTitle = "暂时无法读取记录";
            EmptyText = "导出记录读取失败：" + exception.Message;
            StatusText = "读取失败，请点击刷新重试。";
            CanLoadMore = false;
            App.Services.Diagnostics.Error("导出记录读取失败", exception);
        }
        finally
        {
            IsLoading = false;
            FinishLoad();
        }
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
