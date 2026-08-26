namespace YunKao.Core.Models;

public sealed record ExtractionSessionRecord(
    long Id,
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int QuestionCount,
    int AiCount,
    string Course,
    string SourceUrl = "",
    int DuplicateCount = 0,
    int ErrorCount = 0);

public sealed record ExtractionSessionSnapshot(
    string SessionId,
    DateTimeOffset StartedAt,
    string Status,
    int QuestionCount,
    int AiCount,
    string Course,
    IReadOnlyList<Question> Questions,
    int Current = 0,
    int Total = 0,
    string LastQuestionMarker = "",
    DateTimeOffset? EndedAt = null,
    string SourceUrl = "",
    int DuplicateCount = 0,
    int ErrorCount = 0,
    int AiFailedCount = 0);

public sealed record ExportRecord(
    long Id,
    string Format,
    string FilePath,
    int QuestionCount,
    DateTimeOffset CreatedAt,
    string Status,
    string SessionId = "",
    bool IncludeAnswers = true);

public sealed record DiagnosticRecord(
    long Id,
    DateTimeOffset CreatedAt,
    string Level,
    string Message);
