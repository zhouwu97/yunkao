using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace YunKao.Services;

public enum BackdropMaterial
{
    Mica,
    DesktopAcrylic,
    Solid
}

/// <summary>
/// 窗口材质降级链：工作区优先使用 Desktop Acrylic，不支持时依次降级为 Mica、Solid。
/// </summary>
public static class BackdropService
{
    public static BackdropMaterial AppliedMaterial { get; private set; } = BackdropMaterial.Solid;

    public static BackdropMaterial Apply(Window window, Panel? root, BackdropMaterial preferred = BackdropMaterial.DesktopAcrylic)
    {
        if (IsHighContrastEnabled())
        {
            window.SystemBackdrop = null;
            AppliedMaterial = BackdropMaterial.Solid;
            ApplyRootBackground(root);
            return AppliedMaterial;
        }

        if (preferred == BackdropMaterial.Mica && TrySetMica(window))
        {
            ApplyRootBackground(root);
            return AppliedMaterial;
        }

        if (preferred == BackdropMaterial.DesktopAcrylic && TrySetDesktopAcrylic(window))
        {
            ApplyRootBackground(root);
            return AppliedMaterial;
        }

        if (preferred != BackdropMaterial.Solid && TrySetMica(window))
        {
            ApplyRootBackground(root);
            return AppliedMaterial;
        }

        window.SystemBackdrop = null;
        AppliedMaterial = BackdropMaterial.Solid;
        ApplyRootBackground(root);
        return AppliedMaterial;
    }

    public static BackdropMaterial Apply(Window window, BackdropMaterial preferred = BackdropMaterial.DesktopAcrylic)
    {
        return Apply(window, null, preferred);
    }

    private static void ApplyRootBackground(Panel? root)
    {
        if (root is null)
        {
            return;
        }

        root.Background = new SolidColorBrush(
            AppliedMaterial == BackdropMaterial.Solid
                ? Windows.UI.Color.FromArgb(255, 238, 242, 246)
                : Windows.UI.Color.FromArgb(0, 0, 0, 0));
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

    public static BackdropMaterial Parse(string? material)
    {
        return material?.Trim().ToLowerInvariant() switch
        {
            "mica" => BackdropMaterial.Mica,
            "solid" => BackdropMaterial.Solid,
            _ => BackdropMaterial.DesktopAcrylic,
        };
    }
}
