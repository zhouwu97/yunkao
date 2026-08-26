using YunKao.Core.Models;

namespace YunKao.Core.Services;

public sealed record HistoricalRestoreResult(bool Restored, bool RequiresConfirmation);

/// <summary>
/// 历史会话恢复的生命周期协调器。UI 只负责确认，取消、保存和载入顺序由这里统一执行。
/// </summary>
public sealed class HistoricalSessionRestorer(ExtractionSession session)
{
    private readonly ExtractionSession _session = session;

    public async Task<HistoricalRestoreResult> RestoreAsync(
        ExtractionSessionSnapshot snapshot,
        bool confirmed,
        Action invalidateCurrentCallbacks,
        Func<Task> persistCurrentSession,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(invalidateCurrentCallbacks);
        ArgumentNullException.ThrowIfNull(persistCurrentSession);
        if (!Guid.TryParse(snapshot.SessionId, out Guid snapshotId) || snapshotId == Guid.Empty)
        {
            return new(false, false);
        }

        bool needsConfirmation = RequiresConfirmation(_session);
        if (needsConfirmation && !confirmed)
        {
            return new(false, true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (needsConfirmation)
        {
            // 先让所有在途回调失效，再改变 Session，最后持久化停止后的旧任务。
            invalidateCurrentCallbacks();
            if (_session.Status is ExtractionStatus.Running or ExtractionStatus.Paused or ExtractionStatus.Completing)
            {
                _session.Stop();
            }
            await persistCurrentSession().ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(_session.Restore(snapshot), false);
    }

    public static bool RequiresConfirmation(ExtractionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.Status is ExtractionStatus.Running or ExtractionStatus.Paused or ExtractionStatus.Completing
            || session.SavedCount > 0;
    }
}
