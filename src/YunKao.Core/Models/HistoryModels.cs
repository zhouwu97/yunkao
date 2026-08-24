namespace YunKao.Core.Models;

public sealed record ExtractionSessionRecord(
    long Id,
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int QuestionCount,
    int AiCount,
    string Course);

public sealed record ExportRecord(
    long Id,
    string Format,
    string FilePath,
    int QuestionCount,
    DateTimeOffset CreatedAt,
    string Status);

public sealed record DiagnosticRecord(
    long Id,
    DateTimeOffset CreatedAt,
    string Level,
    string Message);
