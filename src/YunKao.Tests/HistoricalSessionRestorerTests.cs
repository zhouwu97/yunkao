using YunKao.Core.Models;
using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class HistoricalSessionRestorerTests
{
    [Fact]
    public async Task Running_restore_without_confirmation_is_cancelled_without_mutation()
    {
        var session = new ExtractionSession();
        Guid currentId = session.Start();
        session.TryAddQuestion(currentId, new Question { QuestionId = "current", Title = "当前任务" });
        var restorer = new HistoricalSessionRestorer(session);
        bool invalidated = false;
        bool persisted = false;

        HistoricalRestoreResult result = await restorer.RestoreAsync(
            CreateSnapshot(),
            confirmed: false,
            () => invalidated = true,
            () =>
            {
                persisted = true;
                return Task.CompletedTask;
            });

        Assert.False(result.Restored);
        Assert.True(result.RequiresConfirmation);
        Assert.False(invalidated);
        Assert.False(persisted);
        Assert.Equal(currentId, session.SessionId);
        Assert.Equal(ExtractionStatus.Running, session.Status);
        Assert.Equal("当前任务", Assert.Single(session.Questions).Title);
    }

    [Fact]
    public async Task Confirmed_running_restore_invalidates_stops_persists_then_loads_paused_snapshot()
    {
        var session = new ExtractionSession();
        Guid currentId = session.Start();
        session.TryAddQuestion(currentId, new Question { QuestionId = "current", Title = "当前任务" });
        var restorer = new HistoricalSessionRestorer(session);
        var order = new List<string>();

        HistoricalRestoreResult result = await restorer.RestoreAsync(
            CreateSnapshot(),
            confirmed: true,
            () => order.Add("invalidate"),
            () =>
            {
                Assert.Equal(ExtractionStatus.Idle, session.Status);
                order.Add("persist");
                return Task.CompletedTask;
            });

        Assert.True(result.Restored);
        Assert.False(result.RequiresConfirmation);
        Assert.Equal(["invalidate", "persist"], order);
        Assert.Equal(ExtractionStatus.Paused, session.Status);
        Assert.Equal("历史任务", Assert.Single(session.Questions).Title);
        Assert.NotEqual(currentId, session.SessionId);
    }

    [Fact]
    public async Task Idle_restore_can_be_resumed_with_the_restored_session_id()
    {
        var session = new ExtractionSession();
        var restorer = new HistoricalSessionRestorer(session);

        HistoricalRestoreResult result = await restorer.RestoreAsync(
            CreateSnapshot(),
            confirmed: false,
            () => throw new InvalidOperationException("idle restore should not invalidate"),
            () => throw new InvalidOperationException("idle restore should not persist"));

        Assert.True(result.Restored);
        Assert.Equal(ExtractionStatus.Paused, session.Status);
        Guid restoredId = session.SessionId;
        Assert.True(session.ResumeRestored());
        Assert.True(session.TryAddQuestion(restoredId, new Question { QuestionId = "next", Title = "下一题" }));
    }

    [Fact]
    public async Task Cancelled_restore_leaves_the_running_session_untouched()
    {
        var session = new ExtractionSession();
        Guid currentId = session.Start();
        session.TryAddQuestion(currentId, new Question { QuestionId = "current", Title = "当前任务" });
        var restorer = new HistoricalSessionRestorer(session);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => restorer.RestoreAsync(
            CreateSnapshot(),
            confirmed: true,
            () => throw new InvalidOperationException("cancelled restore should not invalidate"),
            () => throw new InvalidOperationException("cancelled restore should not persist"),
            cancellation.Token));

        Assert.Equal(currentId, session.SessionId);
        Assert.Equal(ExtractionStatus.Running, session.Status);
        Assert.Equal("当前任务", Assert.Single(session.Questions).Title);
    }

    private static ExtractionSessionSnapshot CreateSnapshot()
    {
        return new ExtractionSessionSnapshot(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.AddHours(-1),
            "paused",
            1,
            0,
            "历史课程",
            [new Question { QuestionId = "history", Title = "历史任务" }]);
    }
}
