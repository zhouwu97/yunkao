using System.Text.Json;
using Microsoft.Data.Sqlite;
using YunKao.Core.Models;

namespace YunKao.Core.Services;

public sealed class HistoryStore : IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public HistoryStore(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YunKaoDesktop",
            "yunkao.db");
    }

    public string DatabasePath => _databasePath;

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        return ValueTask.CompletedTask;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
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
                questions_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_extraction_sessions_started_at
                ON extraction_sessions(started_at DESC);
            CREATE TABLE IF NOT EXISTS exports (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                format TEXT NOT NULL,
                file_path TEXT NOT NULL,
                question_count INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                status TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS diagnostic_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL,
                level TEXT NOT NULL,
                message TEXT NOT NULL
            );
            UPDATE extraction_sessions
            SET status = 'interrupted'
            WHERE status IN ('Running', 'Paused', 'Completing');
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveSessionAsync(
        ExtractionSession session,
        string course,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM extraction_sessions WHERE session_id = $session_id;
            INSERT INTO extraction_sessions
                (session_id, started_at, completed_at, status, question_count, ai_count, course, questions_json)
            VALUES ($session_id, $started_at, $completed_at, $status, $question_count, $ai_count, $course, $questions_json);
            """;
        command.Parameters.AddWithValue("$session_id", session.SessionId.ToString("N"));
        command.Parameters.AddWithValue("$started_at", (session.StartedAt ?? DateTimeOffset.UtcNow).ToString("O"));
        command.Parameters.AddWithValue("$completed_at", session.Status == ExtractionStatus.Completed ? DateTimeOffset.UtcNow.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$question_count", session.SavedCount);
        command.Parameters.AddWithValue("$ai_count", session.Questions.Count(question => question.AnswerSource == "ai"));
        command.Parameters.AddWithValue("$course", course ?? "");
        command.Parameters.AddWithValue("$questions_json", JsonSerializer.Serialize(session.Questions, _jsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveExportAsync(ExportRecord record, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task AddDiagnosticAsync(DiagnosticRecord record, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO diagnostic_events(created_at, level, message) VALUES ($created_at, $level, $message);";
        command.Parameters.AddWithValue("$created_at", record.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$level", record.Level);
        command.Parameters.AddWithValue("$message", DiagnosticLog.Sanitize(record.Message));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExtractionSessionRecord>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, session_id, started_at, completed_at, status, question_count, ai_count, course FROM extraction_sessions ORDER BY started_at DESC LIMIT 100;";
        var rows = new List<ExtractionSessionRecord>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new ExtractionSessionRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetString(7)));
        }
        return rows;
    }

    public async Task<IReadOnlyList<ExportRecord>> GetExportsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, format, file_path, question_count, created_at, status FROM exports ORDER BY created_at DESC LIMIT 100;";
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

    public async Task<IReadOnlyList<DiagnosticRecord>> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, created_at, level, message FROM diagnostic_events ORDER BY created_at DESC LIMIT 500;";
        var rows = new List<DiagnosticRecord>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new DiagnosticRecord(
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }
}
