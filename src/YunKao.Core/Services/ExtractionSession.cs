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
    public DateTimeOffset? StartedAt { get; private set; }
    public string LastQuestionMarker { get; private set; } = "";
    public IReadOnlyList<Question> Questions
    {
        get { lock (_gate) return _questions.Select(item => item.Clone()).ToArray(); }
    }

    public event EventHandler? Changed;

    public Guid Start(int total = 0)
    {
        lock (_gate)
        {
            SessionId = Guid.NewGuid();
            Status = ExtractionStatus.Running;
            Current = 0;
            Total = Math.Max(0, total);
            AiPending = 0;
            StartedAt = DateTimeOffset.UtcNow;
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
        }

        RaiseChanged();
    }

    public bool TryAddQuestion(Guid sessionId, Question question)
    {
        ArgumentNullException.ThrowIfNull(question);
        bool added;
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
            }
        }

        if (added) RaiseChanged();
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

    public void IncrementAiPending()
    {
        lock (_gate) AiPending++;
        RaiseChanged();
    }

    public void DecrementAiPending()
    {
        lock (_gate)
        {
            AiPending = Math.Max(0, AiPending - 1);
            if (AiPending == 0 && Status == ExtractionStatus.Completing)
            {
                Status = ExtractionStatus.Completed;
            }
        }
        RaiseChanged();
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
                LastQuestionMarker);
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
            LastQuestionMarker = snapshot.LastQuestionMarker ?? "";
            _questions.Clear();
            _seenKeys.Clear();
            foreach (Question question in snapshot.Questions)
            {
                Question clone = question.Clone();
                _questions.Add(clone);
                _seenKeys.Add(QuestionKeyBuilder.Build(clone));
                LastQuestionMarker = clone.Marker;
            }
            Status = ExtractionStatus.Idle;
        }

        RaiseChanged();
        return true;
    }

    public bool ResumeRestored()
    {
        lock (_gate)
        {
            if (Status != ExtractionStatus.Idle || SessionId == Guid.Empty || _questions.Count == 0) return false;
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
