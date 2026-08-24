using Microsoft.UI.Xaml;

namespace YunKao.Behaviors;

/// <summary>
/// 给非按钮的可交互液态岛复用统一 hover/pressed 反馈。
/// </summary>
public static class HoverMotionBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(HoverMotionBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)args.NewValue)
        {
            element.PointerEntered += OnPointerEntered;
            element.PointerExited += OnPointerExited;
            element.PointerPressed += OnPointerPressed;
            element.PointerReleased += OnPointerReleased;
        }
        else
        {
            element.PointerEntered -= OnPointerEntered;
            element.PointerExited -= OnPointerExited;
            element.PointerPressed -= OnPointerPressed;
            element.PointerReleased -= OnPointerReleased;
        }
    }

    private static void OnPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
        => Services.MotionService.AnimateScale((FrameworkElement)sender, 1.015);

    private static void OnPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
        => Services.MotionService.AnimateScale((FrameworkElement)sender, 1);

    private static void OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
        => Services.MotionService.AnimateScale((FrameworkElement)sender, 0.985, 90);

    private static void OnPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
        => Services.MotionService.AnimateScale((FrameworkElement)sender, 1.015, 100);
}
