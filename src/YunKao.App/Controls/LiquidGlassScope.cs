using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace YunKao.Controls;

/// <summary>Shares a decorative source without capturing the glass or its foreground content.</summary>
internal static class LiquidGlassScope
{
    private static readonly ConditionalWeakTable<FrameworkElement, LiquidGlassScene> Scenes = new();

    internal static void SetScene(FrameworkElement scope, LiquidGlassScene scene)
    {
        Scenes.Remove(scope);
        Scenes.Add(scope, scene);
    }

    internal static LiquidGlassScene? Find(DependencyObject surface, bool outermost = false)
    {
        LiquidGlassScene? result = null;
        for (DependencyObject? node = surface; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not FrameworkElement element || !Scenes.TryGetValue(element, out var scene)) continue;
            result = scene;
            if (!outermost) break;
        }
        return result;
    }
}
