using Windows.UI.ViewManagement;
using YunKao.Core.Services;

namespace YunKao.Services;

/// <summary>
/// 全局玻璃材质状态。窗口材质与透明强度变化时通知所有 <see cref="Controls.GlassPanel"/> 重新应用，
/// 系统关闭透明效果或开启高对比度时自动降级为实体表面。
/// </summary>
public static class GlassSettings
{
    private static readonly UISettings SystemSettings = CreateSettings();

    public static double TintOpacity { get; private set; } = 0.60;
    public static bool IsSolidRequested { get; private set; }
    public static bool IsSolid => IsSolidRequested || !SystemTransparencyEnabled || IsHighContrast;
    public static bool BlurEnabled => !IsSolid;
    public static event EventHandler? Changed;

    public static void Update(string material, string clarity, bool appliedSolid)
    {
        IsSolidRequested = appliedSolid || AppearancePolicy.ResolveMaterial(material) == AppearanceMaterial.Solid;
        TintOpacity = AppearancePolicy.GetGlassTintOpacity(IsSolidRequested ? "solid" : material, clarity);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>系统设置（透明效果、高对比度）变化后由窗口调用。</summary>
    public static void Refresh() => Changed?.Invoke(null, EventArgs.Empty);

    private static bool SystemTransparencyEnabled
    {
        get
        {
            try { return SystemSettings.AdvancedEffectsEnabled; }
            catch { return true; }
        }
    }

    public static bool IsHighContrast
    {
        get
        {
            try { return new AccessibilitySettings().HighContrast; }
            catch { return false; }
        }
    }

    private static UISettings CreateSettings()
    {
        var settings = new UISettings();
        return settings;
    }
}
