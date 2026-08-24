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
                    last_question_marker TEXT NOT NULL DEFAULT ''
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
                    status TEXT NOT NULL
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
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task SaveSessionAsync(
        ExtractionSession session,
        string course,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO extraction_sessions
                    (session_id, started_at, completed_at, status, question_count, ai_count, course, questions_json,
                     current_position, total_count, last_question_marker)
                VALUES ($session_id, $started_at, $completed_at, $status, $question_count, $ai_count, $course, $questions_json,
                        $current_position, $total_count, $last_question_marker)
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
                    last_question_marker = excluded.last_question_marker;
                """;
            AddSessionParameters(command, session, course);
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
                INSERT INTO exports(format, file_path, question_count, created_at, status)
                VALUES ($format, $file_path, $question_count, $created_at, $status);
                """;
            command.Parameters.AddWithValue("$format", record.Format);
            command.Parameters.AddWithValue("$file_path", record.FilePath);
            command.Parameters.AddWithValue("$question_count", record.QuestionCount);
            command.Parameters.AddWithValue("$created_at", record.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$status", record.Status);
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

    public async Task<IReadOnlyList<ExtractionSessionSnapshot>> GetInterruptedSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, started_at, status, question_count, ai_count, course, questions_json,
                   current_position, total_count, last_question_marker
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
            SELECT id, session_id, started_at, completed_at, status, question_count, ai_count, course
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
                reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7)));
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
            SELECT id, format, file_path, question_count, created_at, status
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
                DateTimeOffset.Parse(reader.GetString(4)), reader.GetString(5)));
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

    private static void AddSessionParameters(SqliteCommand command, ExtractionSession session, string course)
    {
        command.Parameters.AddWithValue("$session_id", session.SessionId.ToString("N"));
        command.Parameters.AddWithValue("$started_at", (session.StartedAt ?? DateTimeOffset.UtcNow).ToString("O"));
        command.Parameters.AddWithValue("$completed_at", session.Status == ExtractionStatus.Completed
            ? DateTimeOffset.UtcNow.ToString("O")
            : DBNull.Value);
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$question_count", session.SavedCount);
        command.Parameters.AddWithValue("$ai_count", session.Questions.Count(question => question.AnswerSource == "ai"));
        command.Parameters.AddWithValue("$course", course ?? "");
        command.Parameters.AddWithValue("$questions_json", JsonSerializer.Serialize(session.Questions, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        command.Parameters.AddWithValue("$current_position", session.Current);
        command.Parameters.AddWithValue("$total_count", session.Total);
        command.Parameters.AddWithValue("$last_question_marker", session.LastQuestionMarker);
    }

    private ExtractionSessionSnapshot ReadSnapshot(SqliteDataReader reader)
    {
        IReadOnlyList<Question> questions = JsonSerializer.Deserialize<IReadOnlyList<Question>>(reader.GetString(6), _jsonOptions)
            ?? Array.Empty<Question>();
        return new ExtractionSessionSnapshot(
            reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), questions,
            reader.GetInt32(7), reader.GetInt32(8), reader.GetString(9));
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

    private static int ClampLimit(int limit, int fallback) => Math.Clamp(limit <= 0 ? fallback : limit, 1, 500);

    private async Task AppendDiagnosticAsync(Task previous, DiagnosticRecord record, CancellationToken cancellationToken)
    {
        try { await previous.ConfigureAwait(false); } catch { }
        try { await AddDiagnosticAsync(record, cancellationToken).ConfigureAwait(false); } catch { }
    }
}
