using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
/// 统一的液态按钮：局部 Acrylic、小面积 tint、顶部高光和可中断的液体形变反馈。
/// </summary>
public sealed partial class LiquidButton : UserControl
{
    private long _lastSpecularUpdate;
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

    public static readonly DependencyProperty BackdropModeProperty = DependencyProperty.Register(
        nameof(BackdropMode),
        typeof(LiquidBackdropMode),
        typeof(LiquidButton),
        new PropertyMetadata(LiquidBackdropMode.System, OnBackdropModeChanged));

    public static readonly DependencyProperty ShadowEnabledProperty = DependencyProperty.Register(
        nameof(ShadowEnabled),
        typeof(bool),
        typeof(LiquidButton),
        new PropertyMetadata(false, OnShadowEnabledChanged));

    public LiquidButton()
    {
        InitializeComponent();
        Padding = new Thickness(14, 9, 14, 9);
        Loaded += (_, _) =>
        {
            ApplyVariant();
            ApplyBackdropMode();
            ApplyShadow();
        };
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

    public LiquidBackdropMode BackdropMode
    {
        get => (LiquidBackdropMode)GetValue(BackdropModeProperty);
        set => SetValue(BackdropModeProperty, value);
    }

    public bool ShadowEnabled
    {
        get => (bool)GetValue(ShadowEnabledProperty);
        set => SetValue(ShadowEnabledProperty, value);
    }

    private static void OnVariantChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is LiquidButton button) button.ApplyVariant();
    }

    private static void OnBackdropModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is LiquidButton button) button.ApplyBackdropMode();
    }

    private static void OnShadowEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is LiquidButton button) button.ApplyShadow();
    }

    private void ApplyBackdropMode()
    {
        if (BackdropLayer is null) return;
        BackdropLayer.Visibility = BackdropMode == LiquidBackdropMode.System
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyShadow()
    {
        if (VisualRoot is null) return;
        MotionService.SetSoftShadow(VisualRoot, ShadowEnabled);
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
            // 主按钮保留 35~55% 的蓝色玻璃 tint，让 Acrylic backdrop 仍然可见。
            _ => (Color.FromArgb(0xA0, 0x6C, 0x9F, 0xFF), Color.FromArgb(0x78, 0x7E, 0x8F, 0xEB), Colors.White, Color.FromArgb(0x7A, 0xFF, 0xFF, 0xFF)),
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
    private void OnPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        UpdateSpecularPosition(args);
        InteractiveHighlight.Opacity = 1;
        MotionService.AnimateScaleXY(VisualRoot, 1.008, 1.008, 100);
    }

    private void OnPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        InteractiveHighlight.Opacity = 0;
        MotionService.AnimateScaleXY(VisualRoot, 1, 1, 140);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args) => UpdateSpecularPosition(args);

    private void OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        UpdateSpecularPosition(args);
        InteractiveHighlight.Opacity = 1;
        MotionService.AnimateScaleXY(VisualRoot, 1.012, 0.965, 85);
    }

    private void OnPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        InteractiveHighlight.Opacity = 1;
        MotionService.AnimateScaleXY(VisualRoot, 1.008, 1.008, 150);
    }

    private void OnPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        InteractiveHighlight.Opacity = 0;
        MotionService.AnimateScaleXY(VisualRoot, 1, 1, 150);
    }

    private void UpdateSpecularPosition(PointerRoutedEventArgs args)
    {
        long now = Stopwatch.GetTimestamp();
        if (now - _lastSpecularUpdate < Stopwatch.Frequency / 60) return;
        _lastSpecularUpdate = now;
        if (HitTarget.ActualWidth <= 0 || HitTarget.ActualHeight <= 0) return;
        Windows.Foundation.Point point = args.GetCurrentPoint(HitTarget).Position;
        SpecularBrush.Center = new Windows.Foundation.Point(
            Math.Clamp(point.X / HitTarget.ActualWidth, 0, 1),
            Math.Clamp(point.Y / HitTarget.ActualHeight, 0, 1));
        SpecularBrush.GradientOrigin = SpecularBrush.Center;
    }
}
