using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace YunKao.Core.Models;

public sealed class Question
{
    [JsonPropertyName("question_id")]
    public string QuestionId { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = [];

    [JsonPropertyName("question_type")]
    public string QuestionType { get; set; } = "";

    [JsonPropertyName("answer")]
    public string Answer { get; set; } = "";

    [JsonPropertyName("analysis")]
    public string Analysis { get; set; } = "";

    [JsonPropertyName("page_info")]
    public string PageInfo { get; set; } = "";

    [JsonPropertyName("marker")]
    public string Marker { get; set; } = "";

    [JsonPropertyName("answer_source")]
    public string AnswerSource { get; set; } = "dom";

    [JsonPropertyName("analysis_source")]
    public string AnalysisSource { get; set; } = "dom";

    [JsonPropertyName("answer_confidence")]
    public double? AnswerConfidence { get; set; }

    [JsonPropertyName("course")]
    public string Course { get; set; } = "";

    [JsonPropertyName("chapter")]
    public string Chapter { get; set; } = "";

    [JsonIgnore]
    public bool HasAnswer => !string.IsNullOrWhiteSpace(Answer);

    public Question Clone()
    {
        return new Question
        {
            QuestionId = QuestionId,
            Title = Title,
            Options = [.. Options],
            QuestionType = QuestionType,
            Answer = Answer,
            Analysis = Analysis,
            PageInfo = PageInfo,
            Marker = Marker,
            AnswerSource = AnswerSource,
            AnalysisSource = AnalysisSource,
            AnswerConfidence = AnswerConfidence,
            Course = Course,
            Chapter = Chapter,
        };
    }
}

public static class QuestionKeyBuilder
{
    public static string Build(Question question)
    {
        ArgumentNullException.ThrowIfNull(question);
        string source;
        if (!string.IsNullOrWhiteSpace(question.QuestionId))
        {
            source = $"id|{question.QuestionId.Trim()}";
        }
        else
        {
            source = string.Join(
                "|",
                Normalize(question.Course),
                Normalize(question.Chapter),
                Normalize(question.QuestionType),
                Normalize(question.Title),
                string.Join("|", question.Options.Select(Normalize)));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Normalize(string? value)
    {
        return string.Join(" ", (value ?? "").Normalize().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToUpperInvariant();
    }
}
