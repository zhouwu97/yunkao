using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using YunKao.Controls;

namespace YunKao.Services;

internal static class ButtonMotion
{
    private static readonly ConditionalWeakTable<FrameworkElement, object> Attached = new();

    internal static void Attach(FrameworkElement root)
    {
        if (Attached.TryGetValue(root, out _)) return;
        Attached.Add(root, new object());
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            if (MotionService.ReduceMotion) return;
            DependencyObject? node = args.OriginalSource as DependencyObject;
            while (node is not null && node is not Button) node = VisualTreeHelper.GetParent(node);
            if (node is not Button button || !button.IsEnabled || LiquidGlassInteraction.Owns(button)) return;
            // Navigation owns its lens and gesture/selection state independently.
            for (var parent = VisualTreeHelper.GetParent(button); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent.GetType().Name == "NavigationRail") return;
            var interaction = new LiquidGlassInteraction(button, button, () => button.IsEnabled);
            RoutedEventHandler? unloaded = null;
            unloaded = (_, _) => { interaction.Dispose(); button.Unloaded -= unloaded; };
            button.Unloaded += unloaded;
            // The initial press is already bubbling; a newly added button handler has missed it.
            interaction.Begin(args);
        }), true);
    }
}
