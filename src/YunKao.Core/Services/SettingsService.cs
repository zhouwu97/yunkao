using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using YunKao.Core.Models;

namespace YunKao.Core.Services;

public interface ICredentialStore
{
    string? Get(string service, string account);
    void Set(string service, string account, string secret);
    void Delete(string service, string account);
}

public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<(string Service, string Account), string> _values = [];

    public string? Get(string service, string account)
    {
        return _values.TryGetValue((service, account), out string? value) ? value : null;
    }

    public void Set(string service, string account, string secret)
    {
        _values[(service, account)] = secret;
    }

    public void Delete(string service, string account)
    {
        _values.Remove((service, account));
    }
}

/// <summary>
/// Windows Credential Manager Generic Credential 实现，与 Python keyring 的目标保持兼容。
/// </summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    public string? Get(string service, string account)
    {
        CredentialSnapshot? direct = Read(service);
        if (direct is not null && string.Equals(direct.UserName, account, StringComparison.Ordinal))
        {
            return direct.Value;
        }

        CredentialSnapshot? compound = Read(CompoundTarget(account, service));
        return compound is not null && string.Equals(compound.UserName, account, StringComparison.Ordinal)
            ? compound.Value
            : null;
    }

    public void Set(string service, string account, string secret)
    {
        CredentialSnapshot? existing = Read(service);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.UserName))
        {
            // Python keyring 在同一 service 存多个账号时，会把旧值迁移到 account@service。
            Write(CompoundTarget(existing.UserName, service), existing.UserName, existing.Value);
        }

        Write(service, account, secret);
    }

    public void Delete(string service, string account)
    {
        DeleteIfOwned(service, account);
        DeleteIfOwned(CompoundTarget(account, service), account);
    }

    private static void Write(string target, string account, string secret)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(secret ?? "");
        IntPtr blob = Marshal.AllocCoTaskMem(Math.Max(1, bytes.Length));
        try
        {
            if (bytes.Length > 0) Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialType.Generic,
                TargetName = target,
                CredentialBlob = blob,
                CredentialBlobSize = (uint)bytes.Length,
                Persist = CredentialPersistence.LocalMachine,
                UserName = account,
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法写入 Windows Credential Manager。");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static void DeleteIfOwned(string target, string account)
    {
        CredentialSnapshot? existing = Read(target);
        if (existing is not null && string.Equals(existing.UserName, account, StringComparison.Ordinal))
        {
            _ = CredDelete(target, CredentialType.Generic, 0);
        }
    }

    private static string CompoundTarget(string account, string service)
    {
        return $"{account}@{service}";
    }

    private static CredentialSnapshot? Read(string target)
    {
        if (!CredRead(target, CredentialType.Generic, 0, out IntPtr credentialPointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            byte[] bytes = credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0
                ? []
                : new byte[credential.CredentialBlobSize];
            if (bytes.Length > 0) Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return new CredentialSnapshot(credential.UserName ?? "", Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    private sealed record CredentialSnapshot(string UserName, string Value);

    private enum CredentialType : uint
    {
        Generic = 1,
    }

    private enum CredentialPersistence : uint
    {
        LocalMachine = 2,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public CredentialType Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CredentialPersistence Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, CredentialType type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, CredentialType type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}

/// <summary>
/// 配置迁移与凭据边界。settings.json 永远不保存云考密码或 AI Key。
/// </summary>
public sealed class SettingsService
{
    public const int CurrentSchemaVersion = 3;
    public const string AppName = "YunKaoDesktop";
    public const string AiCredentialService = "YunKaoDesktop/AI";
    public const string YunKaoCredentialService = "YunKaoDesktop";
    public const string SchoolCode = "u101441";

    private readonly string _root;
    private readonly ICredentialStore _credentials;
    private readonly IReadOnlyList<string> _legacyFiles;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SettingsService(
        string? appDataRoot = null,
        ICredentialStore? credentials = null,
        IEnumerable<string>? legacyFiles = null)
    {
        _root = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
        _credentials = credentials ?? CreateDefaultCredentialStore();
        _legacyFiles = (legacyFiles ?? DefaultLegacyFiles()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string SettingsPath => Path.Combine(_root, "settings.json");

    public AppSettings Load()
    {
        AppSettings? settings = null;
        bool migrate = false;
        string? legacyAiKey = null;
        string? legacyPassword = null;

        if (File.Exists(SettingsPath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), _jsonOptions);
            }
            catch
            {
                BackupCorruptSettings();
                migrate = true;
            }
        }

        if (settings is null)
        {
            settings = new AppSettings();
            foreach (string legacyFile in _legacyFiles)
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(legacyFile));
                    MapLegacy(document.RootElement, settings, out legacyAiKey, out legacyPassword);
                    migrate = true;
                    break;
                }
                catch { }
            }
        }

        Migrate(settings);
        Normalize(settings);
        string provider = string.IsNullOrWhiteSpace(settings.AiProvider) ? "custom" : settings.AiProvider;
        if (!string.IsNullOrWhiteSpace(legacyAiKey))
        {
            _credentials.Set(AiCredentialService, provider, legacyAiKey);
            migrate = true;
        }
        if (!string.IsNullOrWhiteSpace(legacyPassword) && !string.IsNullOrWhiteSpace(settings.YunKaoUser))
        {
            SetYunKaoPassword(settings.YunKaoUser, legacyPassword, settings.RememberYunKaoPassword);
            migrate = true;
        }

        settings.AiKeySaved = !string.IsNullOrWhiteSpace(_credentials.Get(AiCredentialService, provider));
        if (!File.Exists(SettingsPath) || migrate)
        {
            Save(settings);
        }

        return settings;
    }

    public void Save(AppSettings settings, string? aiKey = null, string? yunkaoPassword = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        if (aiKey is not null)
        {
            SetAiKey(settings.AiProvider, aiKey);
        }

        if (yunkaoPassword is not null && !string.IsNullOrWhiteSpace(settings.YunKaoUser))
        {
            SetYunKaoPassword(settings.YunKaoUser, yunkaoPassword, settings.RememberYunKaoPassword);
        }

        settings.AiKeySaved = !string.IsNullOrWhiteSpace(GetAiKey(settings.AiProvider));
        Directory.CreateDirectory(_root);
        string tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, _jsonOptions), Encoding.UTF8);
        File.Move(tempPath, SettingsPath, true);
    }

    public string GetAiKey(string provider) => _credentials.Get(AiCredentialService, provider) ?? "";

    public void SetAiKey(string provider, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) _credentials.Delete(AiCredentialService, provider);
        else _credentials.Set(AiCredentialService, provider, key.Trim());
    }

    public string GetYunKaoPassword(string user)
    {
        return _credentials.Get(YunKaoCredentialService, $"{SchoolCode}_{user}") ?? "";
    }

    public void SetYunKaoPassword(string user, string password, bool remember)
    {
        string account = $"{SchoolCode}_{user}";
        if (!remember || string.IsNullOrEmpty(password)) _credentials.Delete(YunKaoCredentialService, account);
        else _credentials.Set(YunKaoCredentialService, account, password);
    }

    private static ICredentialStore CreateDefaultCredentialStore()
    {
        return OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new InMemoryCredentialStore();
    }

    private static IEnumerable<string> DefaultLegacyFiles()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, "config.json");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        yield return Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    private static void MapLegacy(
        JsonElement root,
        AppSettings settings,
        out string? aiKey,
        out string? yunKaoPassword)
    {
        aiKey = GetString(root, "ai_api_key");
        yunKaoPassword = GetString(root, "yunkao_password") ?? GetString(root, "password");
        settings.YunKaoUser = GetString(root, "yunkao_user") ?? GetString(root, "user") ?? "";
        settings.RememberYunKaoPassword = GetBool(root, "yunkao_remember_password", true);
        settings.DefaultExportDirectory = GetString(root, "default_export_dir") ?? "";
        settings.ExportPrefix = GetString(root, "export_prefix") ?? GetString(root, "default_filename_prefix") ?? settings.ExportPrefix;
        settings.DefaultExportFormat = GetString(root, "default_export_format") ?? "PDF";
        settings.AutoOpenAfterExport = GetBool(root, "auto_open_after_export", true);
        settings.ExportWithoutAnswers = GetBool(root, "export_without_answers", false);
        settings.AiEnabled = GetBool(root, "ai_auto_fill_missing_answers", false);
        settings.AiProvider = GetString(root, "ai_provider") ?? GetString(root, "ai_mode") ?? settings.AiProvider;
        settings.AiBaseUrl = GetString(root, "ai_base_url") ?? settings.AiBaseUrl;
        settings.AiModel = GetString(root, "ai_model") ?? settings.AiModel;
        settings.AiSupportsImages = GetBool(root, "ai_supports_images", true);
    }

    private static void Normalize(AppSettings settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.AiProvider = string.IsNullOrWhiteSpace(settings.AiProvider) ? "custom" : settings.AiProvider.Trim();
        settings.ExportPrefix = string.IsNullOrWhiteSpace(settings.ExportPrefix) ? "基础题库导出" : settings.ExportPrefix.Trim();
        settings.DefaultExportFormat = string.IsNullOrWhiteSpace(settings.DefaultExportFormat) ? "PDF" : settings.DefaultExportFormat.ToUpperInvariant();
        settings.AppearanceMaterial = settings.AppearanceMaterial.ToLowerInvariant() switch
        {
            "acrylic" or "mica" or "solid" => settings.AppearanceMaterial.ToLowerInvariant(),
            _ => "acrylic",
        };
        settings.AppearanceClarity = settings.AppearanceClarity.ToLowerInvariant() switch
        {
            "clear" or "standard" or "transparent" => settings.AppearanceClarity.ToLowerInvariant(),
            _ => "standard",
        };
    }

    private static void Migrate(AppSettings settings)
    {
        int version = settings.SchemaVersion;
        if (version < 1) version = 1;

        // v1 -> v2：引入可持久化的外观材质和透明度配置。
        if (version < 2)
        {
            settings.AppearanceMaterial = string.IsNullOrWhiteSpace(settings.AppearanceMaterial)
                ? "acrylic"
                : settings.AppearanceMaterial;
            settings.AppearanceClarity = string.IsNullOrWhiteSpace(settings.AppearanceClarity)
                ? "standard"
                : settings.AppearanceClarity;
            version = 2;
        }

        // v2 -> v3：引入系统级减少动画开关。
        if (version < 3)
        {
            version = 3;
        }

        settings.SchemaVersion = version;
    }

    private void BackupCorruptSettings()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            Directory.CreateDirectory(_root);
            string backupPath = Path.Combine(
                _root,
                $"settings.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}.json");
            File.Move(SettingsPath, backupPath, overwrite: false);
        }
        catch
        {
            // 备份失败不能阻止应用继续使用默认设置；诊断会在上层记录加载结果。
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool GetBool(JsonElement root, string name, bool fallback)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }
}
