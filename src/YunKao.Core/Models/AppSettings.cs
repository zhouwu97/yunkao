using System.Text.Json.Serialization;

namespace YunKao.Core.Models;

public sealed class AppSettings
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("yunkao_user")]
    public string YunKaoUser { get; set; } = "";

    [JsonPropertyName("yunkao_remember_password")]
    public bool RememberYunKaoPassword { get; set; } = true;

    [JsonPropertyName("default_export_dir")]
    public string DefaultExportDirectory { get; set; } = "";

    [JsonPropertyName("export_prefix")]
    public string ExportPrefix { get; set; } = "基础题库导出";

    [JsonPropertyName("default_export_format")]
    public string DefaultExportFormat { get; set; } = "PDF";

    [JsonPropertyName("auto_open_after_export")]
    public bool AutoOpenAfterExport { get; set; } = true;

    [JsonPropertyName("export_without_answers")]
    public bool ExportWithoutAnswers { get; set; }

    [JsonPropertyName("ai_enabled")]
    public bool AiEnabled { get; set; }

    [JsonPropertyName("ai_provider")]
    public string AiProvider { get; set; } = "openai";

    [JsonPropertyName("ai_base_url")]
    public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";

    [JsonPropertyName("ai_model")]
    public string AiModel { get; set; } = "gpt-4o-mini";

    [JsonPropertyName("ai_supports_images")]
    public bool AiSupportsImages { get; set; } = true;

    [JsonIgnore]
    public bool AiKeySaved { get; set; }
}
