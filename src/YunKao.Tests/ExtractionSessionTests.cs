using YunKao.Core.Models;
using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class ExtractionSessionTests
{
    [Fact]
    public void A_new_question_is_saved_once_and_old_session_is_rejected()
    {
        var session = new ExtractionSession();
        Guid sessionId = session.Start();
        var question = new Question
        {
            QuestionId = "q-1",
            Title = "同一题",
            Options = ["A. 甲", "B. 乙"],
        };

        Assert.True(session.TryAddQuestion(sessionId, question));
        Assert.False(session.TryAddQuestion(sessionId, question));
        Assert.Single(session.Questions);

        session.Stop();
        Assert.False(session.TryAddQuestion(sessionId, question));
    }

    [Fact]
    public void Completion_waits_for_pending_ai_tasks()
    {
        var session = new ExtractionSession();
        Guid sessionId = session.Start(total: 1);
        Assert.True(session.IncrementAiPending(sessionId));

        Assert.True(session.Complete());
        Assert.Equal(ExtractionStatus.Completing, session.Status);

        Assert.True(session.DecrementAiPending(sessionId));
        Assert.Equal(ExtractionStatus.Completed, session.Status);
    }

    [Fact]
    public void Stale_ai_callback_cannot_mutate_a_restarted_session()
    {
        var session = new ExtractionSession();
        Guid oldSessionId = session.Start(total: 1);
        Assert.True(session.IncrementAiPending(oldSessionId));

        Guid newSessionId = session.Start(total: 2);
        Assert.True(session.IncrementAiPending(newSessionId));

        Assert.False(session.DecrementAiPending(oldSessionId));
        Assert.Equal(1, session.AiPending);
        Assert.Equal(ExtractionStatus.Running, session.Status);
        Assert.False(session.TrySetProgress(oldSessionId, 1, 1));
        Assert.Equal(0, session.Current);
    }

    [Fact]
    public void Clearing_questions_invalidates_inflight_callbacks()
    {
        var session = new ExtractionSession();
        Guid oldSessionId = session.Start();
        session.TryAddQuestion(oldSessionId, new Question { Title = "待清空题目" });
        session.IncrementAiPending(oldSessionId);

        session.Clear();

        Assert.Equal(ExtractionStatus.Idle, session.Status);
        Assert.Equal(0, session.SavedCount);
        Assert.Equal(0, session.AiPending);
        Assert.False(session.TryAddQuestion(oldSessionId, new Question { Title = "旧回调题目" }));
        Assert.False(session.DecrementAiPending(oldSessionId));
    }

    [Fact]
    public void Questions_property_returns_an_export_safe_snapshot()
    {
        var session = new ExtractionSession();
        Guid id = session.Start();
        session.TryAddQuestion(id, new Question { Title = "原始题目", Options = ["A. 原始选项"] });

        Question snapshotQuestion = Assert.Single(session.Questions);
        snapshotQuestion.Title = "已污染的快照";
        snapshotQuestion.Options[0] = "A. 已污染的选项";

        Question storedQuestion = Assert.Single(session.Questions);
        Assert.Equal("原始题目", storedQuestion.Title);
        Assert.Equal("A. 原始选项", storedQuestion.Options[0]);
    }

    [Fact]
    public void Failed_ai_task_is_counted_before_session_completes()
    {
        var session = new ExtractionSession();
        Guid id = session.Start();
        session.IncrementAiPending(id);
        session.Complete();

        Assert.True(session.CompleteAiTask(id, succeeded: false));

        Assert.Equal(ExtractionStatus.Completed, session.Status);
        Assert.Equal(1, session.AiFailedCount);
        Assert.NotNull(session.EndedAt);
    }
}
