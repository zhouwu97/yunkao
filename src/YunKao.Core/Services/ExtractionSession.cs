using YunKao.Core.Models;

namespace YunKao.Core.Services;

public enum ExtractionStatus
{
    Idle,
    Running,
    Paused,
    Completing,
    Completed,
    Error,
}

/// <summary>
/// 提取生命周期的唯一事实来源。UI 只订阅变化，不直接维护题目集合和旧会话判断。
/// </summary>
public sealed class ExtractionSession
{
    private readonly object _gate = new();
    private readonly HashSet<string> _seenKeys = new(StringComparer.Ordinal);
    private readonly List<Question> _questions = [];

    public Guid SessionId { get; private set; }
    public ExtractionStatus Status { get; private set; } = ExtractionStatus.Idle;
    public int Current { get; private set; }
    public int Total { get; private set; }
    public int SavedCount { get { lock (_gate) return _questions.Count; } }
    public int AiPending { get; private set; }
    public int AiFailedCount { get; private set; }
    public int DuplicateCount { get; private set; }
    public int ErrorCount { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string SourceUrl { get; private set; } = "";
    public string LastQuestionMarker { get; private set; } = "";
    public IReadOnlyList<Question> Questions
    {
        get { lock (_gate) return _questions.Select(item => item.Clone()).ToArray(); }
    }

    public event EventHandler? Changed;

    public Guid Start(int total = 0, string sourceUrl = "")
    {
        lock (_gate)
        {
            SessionId = Guid.NewGuid();
            Status = ExtractionStatus.Running;
            Current = 0;
            Total = Math.Max(0, total);
            AiPending = 0;
            AiFailedCount = 0;
            DuplicateCount = 0;
            ErrorCount = 0;
            StartedAt = DateTimeOffset.UtcNow;
            EndedAt = null;
            SourceUrl = sourceUrl ?? "";
            LastQuestionMarker = "";
            _seenKeys.Clear();
            _questions.Clear();
        }

        RaiseChanged();
        return SessionId;
    }

    public bool Pause()
    {
        lock (_gate)
        {
            if (Status != ExtractionStatus.Running) return false;
            Status = ExtractionStatus.Paused;
        }

        RaiseChanged();
        return true;
    }

    public bool Resume()
    {
        lock (_gate)
        {
            if (Status != ExtractionStatus.Paused) return false;
            Status = ExtractionStatus.Running;
        }

        RaiseChanged();
        return true;
    }

    public bool Stop()
    {
        lock (_gate)
        {
            if (Status is ExtractionStatus.Idle or ExtractionStatus.Completed) return false;
            Status = ExtractionStatus.Idle;
            AiPending = 0;
            EndedAt = DateTimeOffset.UtcNow;
        }

        RaiseChanged();
        return true;
    }

    public void Clear()
    {
        lock (_gate)
        {
            SessionId = Guid.NewGuid();
            Status = ExtractionStatus.Idle;
            Current = 0;
            Total = 0;
            AiPending = 0;
            AiFailedCount = 0;
            DuplicateCount = 0;
            ErrorCount = 0;
            StartedAt = null;
            EndedAt = null;
            SourceUrl = "";
            LastQuestionMarker = "";
            _seenKeys.Clear();
            _questions.Clear();
        }

        RaiseChanged();
    }

    public bool Complete()
    {
        lock (_gate)
        {
            if (Status is not (ExtractionStatus.Running or ExtractionStatus.Paused or ExtractionStatus.Completing))
            {
                return false;
            }

            if (AiPending > 0)
            {
                Status = ExtractionStatus.Completing;
            }
            else
            {
                Status = ExtractionStatus.Completed;
                EndedAt = DateTimeOffset.UtcNow;
            }
        }

        RaiseChanged();
        return true;
    }

    public void Fail()
    {
        lock (_gate)
        {
            Status = ExtractionStatus.Error;
            AiPending = 0;
            ErrorCount++;
            EndedAt = DateTimeOffset.UtcNow;
        }

        RaiseChanged();
    }

    public bool TryAddQuestion(Guid sessionId, Question question)
    {
        ArgumentNullException.ThrowIfNull(question);
        bool added;
        bool changed = false;
        lock (_gate)
        {
            if (sessionId != SessionId || Status != ExtractionStatus.Running)
            {
                return false;
            }

            string key = QuestionKeyBuilder.Build(question);
            added = _seenKeys.Add(key);
            if (added)
            {
                _questions.Add(question.Clone());
                LastQuestionMarker = question.Marker;
                changed = true;
            }
            else
            {
                DuplicateCount++;
                changed = true;
            }
        }

        if (changed) RaiseChanged();
        return added;
    }

    public bool TryUpdateQuestion(Guid sessionId, Question question, Action<Question> update)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(update);
        bool updated = false;
        string key = QuestionKeyBuilder.Build(question);
        lock (_gate)
        {
            if (sessionId != SessionId || Status is ExtractionStatus.Idle or ExtractionStatus.Error)
            {
                return false;
            }

            for (int index = 0; index < _questions.Count; index++)
            {
                if (QuestionKeyBuilder.Build(_questions[index]) != key) continue;
                update(_questions[index]);
                updated = true;
                break;
            }
        }

        if (updated) RaiseChanged();
        return updated;
    }

    public void SetProgress(int current, int total, string? marker = null)
    {
        lock (_gate)
        {
            Current = Math.Max(0, current);
            Total = Math.Max(0, total);
            if (marker is not null) LastQuestionMarker = marker;
        }

        RaiseChanged();
    }

    /// <summary>
    /// 仅允许当前会话更新进度，避免旧异步任务覆盖新会话。
    /// </summary>
    public bool TrySetProgress(Guid sessionId, int current, int total, string? marker = null)
    {
        lock (_gate)
        {
            if (sessionId != SessionId) return false;
            Current = Math.Max(0, current);
            Total = Math.Max(0, total);
            if (marker is not null) LastQuestionMarker = marker;
        }

        RaiseChanged();
        return true;
    }

    /// <summary>
    /// 为当前会话登记 AI 任务。已清空或替换的旧会话不能影响新任务计数。
    /// </summary>
    public bool IncrementAiPending(Guid sessionId)
    {
        lock (_gate)
        {
            if (sessionId != SessionId) return false;
            AiPending++;
        }

        RaiseChanged();
        return true;
    }

    /// <summary>
    /// 为当前会话完成 AI 任务。旧回调直接丢弃，防止状态漂移。
    /// </summary>
    public bool CompleteAiTask(Guid sessionId, bool succeeded)
    {
        lock (_gate)
        {
            if (sessionId != SessionId) return false;
            AiPending = Math.Max(0, AiPending - 1);
            if (!succeeded) AiFailedCount++;
            if (AiPending == 0 && Status == ExtractionStatus.Completing)
            {
                Status = ExtractionStatus.Completed;
                EndedAt = DateTimeOffset.UtcNow;
            }
        }
        RaiseChanged();
        return true;
    }

    public bool DecrementAiPending(Guid sessionId) => CompleteAiTask(sessionId, succeeded: true);

    public bool RecordError(Guid sessionId)
    {
        lock (_gate)
        {
            if (sessionId != SessionId) return false;
            ErrorCount++;
        }
        RaiseChanged();
        return true;
    }

    public bool IsCurrent(Guid sessionId)
    {
        lock (_gate)
        {
            return SessionId == sessionId && Status is (ExtractionStatus.Running or ExtractionStatus.Paused);
        }
    }

    public ExtractionSessionSnapshot Snapshot(string course = "")
    {
        lock (_gate)
        {
            return new ExtractionSessionSnapshot(
                SessionId.ToString("N"),
                StartedAt ?? DateTimeOffset.UtcNow,
                Status.ToString(),
                _questions.Count,
                _questions.Count(question => question.AnswerSource == "ai"),
                course,
                _questions.Select(question => question.Clone()).ToArray(),
                Current,
                Total,
                LastQuestionMarker,
                EndedAt,
                SourceUrl,
                DuplicateCount,
                ErrorCount,
                AiFailedCount);
        }
    }

    public bool Restore(ExtractionSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Guid.TryParse(snapshot.SessionId, out Guid sessionId)) return false;

        lock (_gate)
        {
            SessionId = sessionId;
            StartedAt = snapshot.StartedAt;
            Status = Enum.TryParse(snapshot.Status, true, out ExtractionStatus status)
                ? status
                : ExtractionStatus.Idle;
            Current = Math.Max(0, snapshot.Current);
            Total = Math.Max(0, snapshot.Total);
            AiPending = 0;
            AiFailedCount = Math.Max(0, snapshot.AiFailedCount);
            DuplicateCount = Math.Max(0, snapshot.DuplicateCount);
            ErrorCount = Math.Max(0, snapshot.ErrorCount);
            LastQuestionMarker = snapshot.LastQuestionMarker ?? "";
            EndedAt = snapshot.EndedAt;
            SourceUrl = snapshot.SourceUrl ?? "";
            _questions.Clear();
            _seenKeys.Clear();
            foreach (Question question in snapshot.Questions)
            {
                Question clone = question.Clone();
                _questions.Add(clone);
                _seenKeys.Add(QuestionKeyBuilder.Build(clone));
                LastQuestionMarker = clone.Marker;
            }
            Status = ExtractionStatus.Paused;
        }

        RaiseChanged();
        return true;
    }

    public bool ResumeRestored()
    {
        lock (_gate)
        {
            if (Status != ExtractionStatus.Paused || SessionId == Guid.Empty || _questions.Count == 0) return false;
            Status = ExtractionStatus.Running;
        }

        RaiseChanged();
        return true;
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
