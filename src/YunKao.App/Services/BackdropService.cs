using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace YunKao.Services;

public enum BackdropMaterial
{
    DesktopAcrylic,
    Mica,
    Solid
}

/// <summary>
/// 窗口材质降级链：Desktop Acrylic → Mica → Solid。
/// </summary>
public static class BackdropService
{
    public static BackdropMaterial AppliedMaterial { get; private set; } = BackdropMaterial.Solid;

    public static void Apply(Window window, BackdropMaterial preferred)
    {
        if (IsHighContrastEnabled())
        {
            window.SystemBackdrop = null;
            AppliedMaterial = BackdropMaterial.Solid;
            return;
        }

        if (preferred == BackdropMaterial.DesktopAcrylic && TrySetDesktopAcrylic(window))
        {
            return;
        }

        if (preferred != BackdropMaterial.Solid && TrySetMica(window))
        {
            return;
        }

        window.SystemBackdrop = null;
        AppliedMaterial = BackdropMaterial.Solid;
    }

    private static bool TrySetDesktopAcrylic(Window window)
    {
        try
        {
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
            AppliedMaterial = BackdropMaterial.DesktopAcrylic;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TrySetMica(Window window)
    {
        try
        {
            window.SystemBackdrop = new MicaBackdrop();
            AppliedMaterial = BackdropMaterial.Mica;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsHighContrastEnabled()
    {
        try
        {
            return new AccessibilitySettings().HighContrast;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
