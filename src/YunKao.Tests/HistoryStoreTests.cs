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
}
