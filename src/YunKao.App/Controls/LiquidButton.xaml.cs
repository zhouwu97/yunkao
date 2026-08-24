using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YunKao.Services;
using Windows.UI;

namespace YunKao.Controls;

public enum LiquidButtonVariant
{
    Primary,
    Soft,
    Violet,
    Coral,
    Ghost,
}

/// <summary>
/// 统一的液态按钮：小面积 tint、顶部高光和可中断的按压反馈。
/// </summary>
public sealed partial class LiquidButton : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(LiquidButton),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant),
        typeof(LiquidButtonVariant),
        typeof(LiquidButton),
        new PropertyMetadata(LiquidButtonVariant.Primary, OnVariantChanged));

    public LiquidButton()
    {
        InitializeComponent();
        Padding = new Thickness(14, 9, 14, 9);
        Loaded += (_, _) => ApplyVariant();
    }

    public event RoutedEventHandler? Click;

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public LiquidButtonVariant Variant
    {
        get => (LiquidButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private static void OnVariantChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is LiquidButton button) button.ApplyVariant();
    }

    private void ApplyVariant()
    {
        if (Surface is null) return;

        (Color start, Color end, Color foreground, Color border) = Variant switch
        {
            LiquidButtonVariant.Soft => (Color.FromArgb(0x46, 0x6C, 0x9F, 0xFF), Color.FromArgb(0x30, 0x8D, 0xBB, 0xFF), Color.FromArgb(0xFF, 0x4E, 0x78, 0xC8), Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
            LiquidButtonVariant.Violet => (Color.FromArgb(0x5A, 0x8C, 0x7B, 0xE8), Color.FromArgb(0x42, 0xD7, 0x92, 0xB7), Colors.White, Color.FromArgb(0x72, 0xFF, 0xFF, 0xFF)),
            LiquidButtonVariant.Coral => (Color.FromArgb(0x5A, 0xEA, 0x8D, 0x7C), Color.FromArgb(0x42, 0xD8, 0xA4, 0x51), Colors.White, Color.FromArgb(0x72, 0xFF, 0xFF, 0xFF)),
            LiquidButtonVariant.Ghost => (Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0C, 0x62, 0xB7, 0xD8), Color.FromArgb(0xFF, 0x65, 0x79, 0x8E), Color.FromArgb(0x62, 0xFF, 0xFF, 0xFF)),
            _ => (Color.FromArgb(0xFF, 0x6C, 0x9F, 0xFF), Color.FromArgb(0xFF, 0x7E, 0x8F, 0xEB), Colors.White, Color.FromArgb(0x7A, 0xFF, 0xFF, 0xFF)),
        };

        Surface.BorderBrush = new SolidColorBrush(border);
        Foreground = new SolidColorBrush(foreground);
        TintLayer.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = start, Offset = 0 },
                new GradientStop { Color = Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), Offset = 0.5 },
                new GradientStop { Color = end, Offset = 1 },
            },
        };
    }

    private void OnClick(object sender, RoutedEventArgs args) => Click?.Invoke(this, args);
    private void OnPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) => MotionService.AnimateScale(VisualRoot, 1.015);
    private void OnPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) => MotionService.AnimateScale(VisualRoot, 1);
    private void OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) => MotionService.AnimateScale(VisualRoot, 0.985, 90);
    private void OnPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) => MotionService.AnimateScale(VisualRoot, 1.015, 100);
    private void OnPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) => MotionService.AnimateScale(VisualRoot, 1, 100);
}
