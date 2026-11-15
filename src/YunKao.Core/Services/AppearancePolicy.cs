namespace YunKao.Core.Services;

public enum AppearanceMaterial
{
    DesktopAcrylic,
    Mica,
    Solid,
}

public enum AppearanceClarity
{
    Clear,
    Standard,
    Transparent,
}

public enum AppearanceTheme
{
    System,
    Light,
    Dark,
}

public readonly record struct AppearanceSelection(string Material, string Clarity);

/// <summary>
/// 统一外观配置的合法值和降级语义，避免设置页、窗口材质与持久化层各自解释字符串。
/// </summary>
public static class AppearancePolicy
{
    public static AppearanceMaterial ResolveMaterial(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "mica" => AppearanceMaterial.Mica,
            "solid" => AppearanceMaterial.Solid,
            _ => AppearanceMaterial.DesktopAcrylic,
        };
    }

    public static AppearanceClarity ResolveClarity(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "clear" => AppearanceClarity.Clear,
            "transparent" => AppearanceClarity.Transparent,
            _ => AppearanceClarity.Standard,
        };
    }

    public static AppearanceTheme ResolveTheme(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "light" => AppearanceTheme.Light,
            "dark" => AppearanceTheme.Dark,
            _ => AppearanceTheme.System,
        };
    }

    public static string NormalizeTheme(string? value)
    {
        return ResolveTheme(value) switch
        {
            AppearanceTheme.Light => "light",
            AppearanceTheme.Dark => "dark",
            _ => "system",
        };
    }

    /// <summary>
    /// 玻璃着色层的不透明度。透明强度越高，桌面与环境光透出越多；纯色材质始终完全不透明。
    /// </summary>
    public static double GetGlassTintOpacity(string? material, string? clarity)
    {
        if (ResolveMaterial(material) == AppearanceMaterial.Solid) return 1.0;
        return ResolveClarity(clarity) switch
        {
            AppearanceClarity.Clear => 0.82,
            AppearanceClarity.Transparent => 0.40,
            _ => 0.60,
        };
    }

    public static string NormalizeMaterial(string? value)
    {
        return ResolveMaterial(value) switch
        {
            AppearanceMaterial.Mica => "mica",
            AppearanceMaterial.Solid => "solid",
            _ => "acrylic",
        };
    }

    public static string NormalizeClarity(string? value)
    {
        return ResolveClarity(value) switch
        {
            AppearanceClarity.Clear => "clear",
            AppearanceClarity.Transparent => "transparent",
            _ => "standard",
        };
    }
}

/// <summary>
/// 管理设置页的外观预览基线。预览只改变运行时，提交后才更新可恢复配置。
/// </summary>
public sealed class AppearancePreviewState
{
    public AppearanceSelection Saved { get; private set; } = new("acrylic", "standard");
    public bool IsDirty { get; private set; }

    public void Begin(string? material, string? clarity)
    {
        Saved = Normalize(material, clarity);
        IsDirty = false;
    }

    public void Preview(string? material, string? clarity)
    {
        IsDirty = Normalize(material, clarity) != Saved;
    }

    public void Commit(string? material, string? clarity)
    {
        Begin(material, clarity);
    }

    public bool TryGetRollback(out AppearanceSelection selection)
    {
        if (!IsDirty)
        {
            selection = default;
            return false;
        }

        selection = Saved;
        IsDirty = false;
        return true;
    }

    private static AppearanceSelection Normalize(string? material, string? clarity)
    {
        return new(
            AppearancePolicy.NormalizeMaterial(material),
            AppearancePolicy.NormalizeClarity(clarity));
    }
}
