using System.Text.Json;
using Microsoft.Data.Sqlite;
using YunKao.Core.Models;

namespace YunKao.Core.Services;

/// <summary>
/// SQLite 历史仓库。初始化只执行一次，所有写操作串行化，避免导出进度和诊断风暴并发写库。
/// </summary>
public sealed class HistoryStore : IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _diagnosticQueueGate = new();
    private Task _diagnosticTail = Task.CompletedTask;
    private bool _initialized;

    public HistoryStore(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YunKaoDesktop",
            "yunkao.db");
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            string? directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS extraction_sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT NULL,
                    status TEXT NOT NULL,
                    question_count INTEGER NOT NULL,
                    ai_count INTEGER NOT NULL,
                    course TEXT NOT NULL,
                    questions_json TEXT NOT NULL,
                    current_position INTEGER NOT NULL DEFAULT 0,
                    total_count INTEGER NOT NULL DEFAULT 0,
                    last_question_marker TEXT NOT NULL DEFAULT '',
                    source_url TEXT NOT NULL DEFAULT '',
                    duplicate_count INTEGER NOT NULL DEFAULT 0,
                    error_count INTEGER NOT NULL DEFAULT 0,
                    ai_failed_count INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS ix_extraction_sessions_started_at
                    ON extraction_sessions(started_at DESC);
                DELETE FROM extraction_sessions
                    WHERE id NOT IN (SELECT MAX(id) FROM extraction_sessions GROUP BY session_id);
                CREATE UNIQUE INDEX IF NOT EXISTS ux_extraction_sessions_session_id
                    ON extraction_sessions(session_id);
                CREATE TABLE IF NOT EXISTS exports (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    format TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    question_count INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    status TEXT NOT NULL,
                    session_id TEXT NOT NULL DEFAULT '',
                    include_answers INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS ix_exports_created_at
                    ON exports(created_at DESC);
                CREATE TABLE IF NOT EXISTS diagnostic_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_at TEXT NOT NULL,
                    level TEXT NOT NULL,
                    message TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_diagnostic_events_created_at
                    ON diagnostic_events(created_at DESC);
                UPDATE extraction_sessions
                SET status = 'interrupted'
                WHERE status IN ('Running', 'Paused', 'Completing');
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSessionColumnAsync(connection, "current_position", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
            await EnsureSessionColumnAsync(connection, "total_count", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
            await EnsureSessionColumnAsync(connection, "last_question_marker", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
            await EnsureSessionColumnAsync(connection, "source_url", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
            await EnsureSessionColumnAsync(connection, "duplicate_count", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
            await EnsureSessionColumnAsync(connection, "error_count", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
            await EnsureSessionColumnAsync(connection, "ai_failed_count", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
            await EnsureExportColumnAsync(connection, "session_id", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
            await EnsureExportColumnAsync(connection, "include_answers", "INTEGER NOT NULL DEFAULT 1", cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public Task SaveSessionAsync(
        ExtractionSession session,
        string course,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        // 在等待数据库锁之前冻结快照，避免会话恢复/重启期间读到另一代 Session。
        ExtractionSessionSnapshot snapshot = session.Snapshot(course);
        return SaveSessionSnapshotAsync(snapshot, cancellationToken);
    }

    public async Task SaveSessionSnapshotAsync(
        ExtractionSessionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO extraction_sessions
                    (session_id, started_at, completed_at, status, question_count, ai_count, course, questions_json,
                     current_position, total_count, last_question_marker, source_url, duplicate_count, error_count, ai_failed_count)
                VALUES ($session_id, $started_at, $completed_at, $status, $question_count, $ai_count, $course, $questions_json,
                        $current_position, $total_count, $last_question_marker, $source_url, $duplicate_count, $error_count, $ai_failed_count)
                ON CONFLICT(session_id) DO UPDATE SET
                    started_at = excluded.started_at,
                    completed_at = excluded.completed_at,
                    status = excluded.status,
                    question_count = excluded.question_count,
                    ai_count = excluded.ai_count,
                    course = excluded.course,
                    questions_json = excluded.questions_json,
                    current_position = excluded.current_position,
                    total_count = excluded.total_count,
                    last_question_marker = excluded.last_question_marker,
                    source_url = excluded.source_url,
                    duplicate_count = excluded.duplicate_count,
                    error_count = excluded.error_count,
                    ai_failed_count = excluded.ai_failed_count;
                """;
            AddSessionParameters(command, snapshot);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task SaveExportAsync(ExportRecord record, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO exports(format, file_path, question_count, created_at, status, session_id, include_answers)
                VALUES ($format, $file_path, $question_count, $created_at, $status, $session_id, $include_answers);
                """;
            command.Parameters.AddWithValue("$format", record.Format);
            command.Parameters.AddWithValue("$file_path", record.FilePath);
            command.Parameters.AddWithValue("$question_count", record.QuestionCount);
            command.Parameters.AddWithValue("$created_at", record.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$status", record.Status);
            command.Parameters.AddWithValue("$session_id", record.SessionId ?? "");
            command.Parameters.AddWithValue("$include_answers", record.IncludeAnswers ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task AddDiagnosticAsync(DiagnosticRecord record, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO diagnostic_events(created_at, level, message) VALUES ($created_at, $level, $message);";
            command.Parameters.AddWithValue("$created_at", record.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$level", record.Level);
            command.Parameters.AddWithValue("$message", DiagnosticLog.Sanitize(record.Message));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task QueueDiagnosticAsync(DiagnosticRecord record, CancellationToken cancellationToken = default)
    {
        lock (_diagnosticQueueGate)
        {
            _diagnosticTail = AppendDiagnosticAsync(_diagnosticTail, record, cancellationToken);
            return _diagnosticTail;
        }
    }

    public Task<IReadOnlyList<ExtractionSessionRecord>> GetSessionsAsync(
        DateTimeOffset? before = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
        => ReadSessionsAsync(before, limit, cancellationToken);

    public Task<IReadOnlyList<ExportRecord>> GetExportsAsync(
        DateTimeOffset? before = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
        => ReadExportsAsync(before, limit, cancellationToken);

    public Task<IReadOnlyList<DiagnosticRecord>> GetDiagnosticsAsync(
        DateTimeOffset? before = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
        => ReadDiagnosticsAsync(before, limit, cancellationToken);

    /// <summary>
    /// 返回导出关联的题目快照，供导出中心安全地重新导出。
    /// </summary>
    public async Task<ExtractionSessionSnapshot?> GetSessionSnapshotAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, started_at, status, question_count, ai_count, course, questions_json,
                   current_position, total_count, last_question_marker, completed_at, source_url, duplicate_count, error_count, ai_failed_count
            FROM extraction_sessions
            WHERE session_id = $session_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadSnapshot(reader) : null;
    }

    /// <summary>
    /// 清空本次题目时移除其可恢复快照，避免下次启动把已明确丢弃的会话误判为中断任务。
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM extraction_sessions WHERE session_id = $session_id;";
            command.Parameters.AddWithValue("$session_id", sessionId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// 仅删除本地记录，不删除用户已经生成的文件。
    /// </summary>
    public async Task DeleteExportAsync(long exportId, CancellationToken cancellationToken = default)
    {
        if (exportId <= 0) return;
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM exports WHERE id = $id;";
            command.Parameters.AddWithValue("$id", exportId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<ExtractionSessionSnapshot>> GetInterruptedSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, started_at, status, question_count, ai_count, course, questions_json,
                   current_position, total_count, last_question_marker, completed_at, source_url, duplicate_count, error_count, ai_failed_count
            FROM extraction_sessions
            WHERE status = 'interrupted'
            ORDER BY started_at DESC;
            """;
        var rows = new List<ExtractionSessionSnapshot>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(ReadSnapshot(reader));
        return rows;
    }

    public async ValueTask DisposeAsync()
    {
        try { await _diagnosticTail.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
        SqliteConnection.ClearAllPools();
        _writeGate.Dispose();
        _initializeGate.Dispose();
    }

    private async Task<IReadOnlyList<ExtractionSessionRecord>> ReadSessionsAsync(
        DateTimeOffset? before, int limit, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, session_id, started_at, completed_at, status, question_count, ai_count, course,
                   source_url, duplicate_count, error_count
            FROM extraction_sessions
            WHERE ($before IS NULL OR started_at < $before)
            ORDER BY started_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$before", before?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$limit", ClampLimit(limit, 50));
        var rows = new List<ExtractionSessionRecord>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new ExtractionSessionRecord(
                reader.GetInt64(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)), reader.GetString(4),
                reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7), reader.GetString(8),
                reader.GetInt32(9), reader.GetInt32(10)));
        }
        return rows;
    }

    private async Task<IReadOnlyList<ExportRecord>> ReadExportsAsync(
        DateTimeOffset? before, int limit, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, format, file_path, question_count, created_at, status, session_id, include_answers
            FROM exports
            WHERE ($before IS NULL OR created_at < $before)
            ORDER BY created_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$before", before?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$limit", ClampLimit(limit, 50));
        var rows = new List<ExportRecord>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new ExportRecord(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                DateTimeOffset.Parse(reader.GetString(4)), reader.GetString(5),
                reader.GetString(6), reader.GetInt32(7) != 0));
        }
        return rows;
    }

    private async Task<IReadOnlyList<DiagnosticRecord>> ReadDiagnosticsAsync(
        DateTimeOffset? before, int limit, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_at, level, message
            FROM diagnostic_events
            WHERE ($before IS NULL OR created_at < $before)
            ORDER BY created_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$before", before?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$limit", ClampLimit(limit, 200));
        var rows = new List<DiagnosticRecord>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new DiagnosticRecord(
                reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3)));
        }
        return rows;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private SqliteConnection CreateConnection() => new($"Data Source={_databasePath};Pooling=False");

    private static void AddSessionParameters(SqliteCommand command, ExtractionSessionSnapshot snapshot)
    {
        command.Parameters.AddWithValue("$session_id", snapshot.SessionId);
        command.Parameters.AddWithValue("$started_at", snapshot.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$completed_at", snapshot.EndedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", snapshot.Status);
        command.Parameters.AddWithValue("$question_count", snapshot.Questions.Count);
        command.Parameters.AddWithValue("$ai_count", snapshot.Questions.Count(question => question.AnswerSource == "ai"));
        command.Parameters.AddWithValue("$course", snapshot.Course ?? "");
        command.Parameters.AddWithValue("$questions_json", JsonSerializer.Serialize(snapshot.Questions, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        command.Parameters.AddWithValue("$current_position", snapshot.Current);
        command.Parameters.AddWithValue("$total_count", snapshot.Total);
        command.Parameters.AddWithValue("$last_question_marker", snapshot.LastQuestionMarker ?? "");
        command.Parameters.AddWithValue("$source_url", snapshot.SourceUrl ?? "");
        command.Parameters.AddWithValue("$duplicate_count", snapshot.DuplicateCount);
        command.Parameters.AddWithValue("$error_count", snapshot.ErrorCount);
        command.Parameters.AddWithValue("$ai_failed_count", snapshot.AiFailedCount);
    }

    private ExtractionSessionSnapshot ReadSnapshot(SqliteDataReader reader)
    {
        IReadOnlyList<Question> questions = JsonSerializer.Deserialize<IReadOnlyList<Question>>(reader.GetString(6), _jsonOptions)
            ?? Array.Empty<Question>();
        return new ExtractionSessionSnapshot(
            reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), questions,
            reader.GetInt32(7), reader.GetInt32(8), reader.GetString(9),
            reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            reader.GetString(11), reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14));
    }

    private static async Task EnsureSessionColumnAsync(
        SqliteConnection connection,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE extraction_sessions ADD COLUMN {columnName} {definition};";
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // 旧版本数据库已经完成过该列迁移，继续执行其余启动流程。
        }
    }

    private static async Task EnsureExportColumnAsync(
        SqliteConnection connection,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE exports ADD COLUMN {columnName} {definition};";
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // 已升级过的本地数据库无需重复迁移。
        }
    }

    private static int ClampLimit(int limit, int fallback) => Math.Clamp(limit <= 0 ? fallback : limit, 1, 500);

    private async Task AppendDiagnosticAsync(Task previous, DiagnosticRecord record, CancellationToken cancellationToken)
    {
        try { await previous.ConfigureAwait(false); } catch { }
        try { await AddDiagnosticAsync(record, cancellationToken).ConfigureAwait(false); } catch { }
    }
}
