using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using YunKao.Core.Presentation;
using YunKao.Core.Services;

namespace YunKao.ViewModels;

/// <summary>提取任务控制器对界面暴露的操作。</summary>
public interface ITaskActions
{
    void Start();
    void TogglePause();
    void Stop();
    void Clear();
    void Restart();
    void RestoreInterrupted();
    void Export(string format);
    void ExportDefault();
    void SetPracticeMode(bool enabled);
}

public sealed record ActivityEntry(string Time, string Message, bool IsWarning);

/// <summary>
/// 当前提取任务的界面状态。应用级单例：页面销毁重建都绑定同一份状态。
/// 所有更新方法都可以在任意线程调用，内部统一切回 UI 线程。
/// </summary>
public sealed class TaskViewModel : ObservableObject
{
    private const int ActivityLimit = 80;
    private ITaskActions? _actions;
    private TaskPresentationState _presentation = TaskPresentation.From(default);
    private TaskSnapshot _snapshot;
    private int _current;
    private int _total;
    private int _aiPending;
    private bool _isExporting;
    private string _exportStateText = "待机";
    private bool _practiceMode;
    private int? _interruptedCount;
    private string _aiStatus = "未启用";
    private string _aiDetail = "缺少答案时自动补全，可在设置中开启";

    public TaskViewModel()
    {
        PrimaryCommand = new RelayCommand(ExecutePrimary, () => _presentation.IsActionEnabled && _actions is not null);
        StopCommand = new RelayCommand(() => _actions?.Stop(), () => _presentation.ShowStop);
        ClearCommand = new RelayCommand(() => _actions?.Clear(), () => _presentation.CanClear);
        RestartCommand = new RelayCommand(() => _actions?.Restart(), () => _presentation.CanRestart);
        RestoreCommand = new RelayCommand(() => _actions?.RestoreInterrupted(), () => _interruptedCount is > 0);
        ExportCommand = new RelayCommand(
            parameter => _actions?.Export(parameter?.ToString() ?? "pdf"),
            _ => HasQuestions && !_isExporting);
    }

    public DispatcherQueue? Dispatcher { get; set; }

    public ObservableCollection<ActivityEntry> Activity { get; } = [];

    public RelayCommand PrimaryCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand RestartCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand ExportCommand { get; }

    // ---- 面板文案（来自 TaskPresentation） ----
    public string Glyph => _presentation.Glyph;
    public string StateText => _presentation.StateText;
    public string StepText => _presentation.StepText;
    public string HintText => _presentation.HintText;
    public string ActionLabel => _presentation.ActionLabel;
    public bool IsActionEnabled => _presentation.IsActionEnabled;
    public TaskActionTone Tone => _presentation.Tone;
    public bool ShowStop => _presentation.ShowStop;
    public bool CanClear => _presentation.CanClear;
    public bool CanRestart => _presentation.CanRestart;
    public TaskStatusKind StatusKind => _presentation.StatusKind;
    public string StatusLabel => _presentation.StatusLabel;

    // ---- 进度 ----
    public int SavedCount => _snapshot.SavedCount;
    public bool HasQuestions => _snapshot.SavedCount > 0;
    public int Current => _current;
    public int Total => _total;
    public int AiPending => _aiPending;
    public bool HasProgress => _total > 0;
    public double ProgressPercent => _total > 0 ? Math.Clamp(_current * 100d / _total, 0, 100) : 0;
    public string ProgressText => _total > 0 || SavedCount > 0
        ? $"已保存 {SavedCount} 题 · AI 待处理 {_aiPending}"
        : "尚未开始";

    // ---- 导出 ----
    public bool IsExporting => _isExporting;
    public bool CanExport => HasQuestions && !_isExporting;
    public string ExportStateText => _exportStateText;

    public bool PracticeMode
    {
        get => _practiceMode;
        set
        {
            if (!SetProperty(ref _practiceMode, value)) return;
            _actions?.SetPracticeMode(value);
        }
    }

    // ---- 中断恢复 ----
    public bool HasInterrupted => _interruptedCount is > 0;
    public string RestoreLabel => _interruptedCount is > 0 ? $"恢复上次任务（{_interruptedCount} 题）" : "恢复上次任务";

    // ---- AI ----
    public string AiStatus => _aiStatus;
    public string AiDetail => _aiDetail;

    public void Attach(ITaskActions actions)
    {
        _actions = actions;
        Post(RefreshCommands);
    }

    public void Detach(ITaskActions actions)
    {
        if (ReferenceEquals(_actions, actions)) _actions = null;
        Post(RefreshCommands);
    }

    public void ApplySnapshot(TaskSnapshot snapshot) => Post(() =>
    {
        bool savedChanged = snapshot.SavedCount != _snapshot.SavedCount;
        _snapshot = snapshot;
        _presentation = TaskPresentation.From(snapshot);
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(IsActionEnabled));
        OnPropertyChanged(nameof(Tone));
        OnPropertyChanged(nameof(ShowStop));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(StatusLabel));
        if (savedChanged)
        {
            OnPropertyChanged(nameof(SavedCount));
            OnPropertyChanged(nameof(HasQuestions));
            OnPropertyChanged(nameof(CanExport));
            OnPropertyChanged(nameof(ProgressText));
            if (!_isExporting) SetExportState(HasQuestions ? "就绪" : "待机");
        }
        RefreshCommands();
    });

    public void SetProgress(int current, int total, int aiPending) => Post(() =>
    {
        _current = current;
        _total = total;
        _aiPending = aiPending;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(AiPending));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressText));
    });

    public void SetExporting(bool exporting) => Post(() =>
    {
        _isExporting = exporting;
        OnPropertyChanged(nameof(IsExporting));
        OnPropertyChanged(nameof(CanExport));
        SetExportState(exporting ? "正在导出…" : HasQuestions ? "就绪" : "待机");
        RefreshCommands();
    });

    public void SetExportProgress(int current, int total, string message)
        => Post(() => SetExportState(total > 0 ? $"{current}/{total}" : message));

    /// <summary>从配置同步练习版开关，不回写配置。</summary>
    public void SetPracticeMode(bool enabled) => Post(() =>
    {
        if (_practiceMode == enabled) return;
        _practiceMode = enabled;
        OnPropertyChanged(nameof(PracticeMode));
    });

    public void SetInterrupted(int? questionCount) => Post(() =>
    {
        _interruptedCount = questionCount is > 0 ? questionCount : null;
        OnPropertyChanged(nameof(HasInterrupted));
        OnPropertyChanged(nameof(RestoreLabel));
        RestoreCommand.NotifyCanExecuteChanged();
    });

    public void SetAiStatus(string status, string detail) => Post(() =>
    {
        _aiStatus = status;
        _aiDetail = detail;
        OnPropertyChanged(nameof(AiStatus));
        OnPropertyChanged(nameof(AiDetail));
    });

    public void AddActivity(string message, bool warning = false) => Post(() =>
    {
        Activity.Insert(0, new ActivityEntry(DateTime.Now.ToString("HH:mm:ss"), message, warning));
        while (Activity.Count > ActivityLimit) Activity.RemoveAt(Activity.Count - 1);
    });

    private void ExecutePrimary()
    {
        if (_actions is null) return;
        switch (_presentation.PrimaryAction)
        {
            case TaskPrimaryAction.Start:
                _actions.Start();
                break;
            case TaskPrimaryAction.Pause:
            case TaskPrimaryAction.Resume:
                _actions.TogglePause();
                break;
            case TaskPrimaryAction.Export:
                _actions.ExportDefault();
                break;
            case TaskPrimaryAction.Restart:
                _actions.Restart();
                break;
        }
    }

    private void SetExportState(string text)
    {
        if (_exportStateText == text) return;
        _exportStateText = text;
        OnPropertyChanged(nameof(ExportStateText));
    }

    private void RefreshCommands()
    {
        PrimaryCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    private void Post(Action action)
    {
        DispatcherQueue? dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess) action();
        else dispatcher.TryEnqueue(() => action());
    }
}
