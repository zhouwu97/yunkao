using System.Text.Json;
using YunKao.Core.Models;
using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class SettingsServiceTests
{
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
}
