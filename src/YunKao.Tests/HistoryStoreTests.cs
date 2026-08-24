using YunKao.Core.Models;
using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class HistoryStoreTests
{
    [Fact]
    public async Task Persists_session_and_export_records_in_sqlite()
    {
        string root = Path.Combine(Path.GetTempPath(), "yunkao-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new HistoryStore(Path.Combine(root, "yunkao.db"));
            var session = new ExtractionSession();
            Guid id = session.Start();
            session.TryAddQuestion(id, new Question { Title = "测试题" });
            session.Complete();
            await store.SaveSessionAsync(session, "测试课程");
            await store.SaveExportAsync(new ExportRecord(0, "MD", "test.md", 1, DateTimeOffset.UtcNow, "completed"));

            Assert.Single(await store.GetSessionsAsync());
            Assert.Single(await store.GetExportsAsync());
            await store.DisposeAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Diagnostic_log_redacts_sensitive_fields()
    {
        Assert.DoesNotContain("secret", DiagnosticLog.Sanitize("api_key=secret Authorization=Bearer secret"));
        Assert.Contains("[REDACTED]", DiagnosticLog.Sanitize("password=secret"));
    }

    [Fact]
    public async Task Concurrent_session_saves_upsert_one_row()
    {
        string root = Path.Combine(Path.GetTempPath(), "yunkao-history-upsert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var store = new HistoryStore(Path.Combine(root, "yunkao.db"));
            var session = new ExtractionSession();
            Guid id = session.Start();
            session.TryAddQuestion(id, new Question { Title = "并发题" });

            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.SaveSessionAsync(session, "并发课程")));

            Assert.Single(await store.GetSessionsAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Persists_interrupted_session_position_for_resume()
    {
        string root = Path.Combine(Path.GetTempPath(), "yunkao-history-resume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string databasePath = Path.Combine(root, "yunkao.db");
        try
        {
            var session = new ExtractionSession();
            Guid id = session.Start();
            session.TryAddQuestion(id, new Question { Title = "第一题", Marker = "marker-1" });
            session.SetProgress(1, 10, "marker-1");

            await using (var store = new HistoryStore(databasePath))
            {
                await store.SaveSessionAsync(session, "恢复课程");
            }

            await using var restartedStore = new HistoryStore(databasePath);
            ExtractionSessionSnapshot snapshot = Assert.Single(await restartedStore.GetInterruptedSessionsAsync());
            Assert.Equal(1, snapshot.Current);
            Assert.Equal(10, snapshot.Total);
            Assert.Equal("marker-1", snapshot.LastQuestionMarker);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
