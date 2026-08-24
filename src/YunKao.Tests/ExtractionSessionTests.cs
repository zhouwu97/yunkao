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
        session.Start(total: 1);
        session.IncrementAiPending();

        Assert.True(session.Complete());
        Assert.Equal(ExtractionStatus.Completing, session.Status);

        session.DecrementAiPending();
        Assert.Equal(ExtractionStatus.Completed, session.Status);
    }
}
