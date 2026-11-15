using System.Text.Json;
using YunKao.Core.Models;
using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class SettingsServiceTests
{
    [Theory]
    [InlineData("DOCX", "DOCX")]
    [InlineData(" txt ", "TXT")]
    [InlineData("Markdown", "MD")]
    [InlineData(".md", "MD")]
    [InlineData("unsupported", "PDF")]
    [InlineData("", "PDF")]
    public void Loads_legacy_export_format_and_preserves_it_after_saving(string storedFormat, string expected)
    {
        string root = Path.Combine(Path.GetTempPath(), "yunkao-settings-format-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.json"),
                JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["schema_version"] = SettingsService.CurrentSchemaVersion,
                    ["default_export_format"] = storedFormat,
                }));
            var service = new SettingsService(root, new InMemoryCredentialStore());
            AppSettings settings = service.Load();
            Assert.Equal(expected, settings.DefaultExportFormat);
            service.Save(settings);
            Assert.Equal(expected, service.Load().DefaultExportFormat);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Migrates_legacy_ai_key_to_credential_store_without_writing_it_to_json()
    {
        string root = Path.Combine(Path.GetTempPath(), "yunkao-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string legacy = Path.Combine(root, "config.json");
            File.WriteAllText(legacy, "{\"ai_provider\":\"custom\",\"ai_api_key\":\"secret-key\",\"yunkao_user\":\"20260001\",\"yunkao_password\":\"local-password\"}");

            var credentials = new InMemoryCredentialStore();
            var service = new SettingsService(root, credentials, [legacy]);
            AppSettings settings = service.Load();

            Assert.Equal("20260001", settings.YunKaoUser);
            Assert.Equal("secret-key", credentials.Get(SettingsService.AiCredentialService, "custom"));
            Assert.Equal("local-password", credentials.Get(SettingsService.YunKaoCredentialService, "u101441_20260001"));

            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "settings.json")));
            Assert.False(json.RootElement.TryGetProperty("ai_api_key", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Backs_up_corrupt_settings_before_recreating_defaults()
    {
        string root = Path.Combine(Path.GetTempPath(), "yunkao-settings-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string settingsPath = Path.Combine(root, "settings.json");
            File.WriteAllText(settingsPath, "{ this is not json");

            var service = new SettingsService(root, new InMemoryCredentialStore());
            service.Load();

            Assert.NotEmpty(Directory.GetFiles(root, "settings.corrupt-*.json"));
            Assert.DoesNotContain("this is not json", File.ReadAllText(settingsPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Persists_supported_appearance_settings()
    {
        string root = Path.Combine(Path.GetTempPath(), "yunkao-settings-appearance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new SettingsService(root, new InMemoryCredentialStore());
            var settings = service.Load();
            settings.AppearanceMaterial = "mica";
            settings.AppearanceClarity = "transparent";

            service.Save(settings);
            AppSettings reloaded = service.Load();

            Assert.Equal("mica", reloaded.AppearanceMaterial);
            Assert.Equal("transparent", reloaded.AppearanceClarity);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Normalizes_invalid_appearance_settings_to_safe_defaults()
    {
        string root = Path.Combine(Path.GetTempPath(), "yunkao-settings-appearance-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "settings.json"),
                "{\"appearance_material\":\"unknown\",\"appearance_clarity\":null}");

            var service = new SettingsService(root, new InMemoryCredentialStore());
            AppSettings settings = service.Load();

            Assert.Equal("acrylic", settings.AppearanceMaterial);
            Assert.Equal("standard", settings.AppearanceClarity);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
