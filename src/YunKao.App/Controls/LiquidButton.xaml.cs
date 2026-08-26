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
/// 统一的液态按钮：局部 backdrop、小面积 tint、边缘高光和可中断的软玻璃形变反馈。
/// </summary>
public sealed partial class LiquidButton : UserControl
{
    private long _lastSpecularUpdate;
    private bool _pointerInside;
    private bool _pressed;
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
        if (CompositionBackdropLayer is null || SystemBackdropLayer is null) return;

        bool wantsBackdrop = BackdropMode == LiquidBackdropMode.System;
        CompositionBackdropLayer.Visibility = wantsBackdrop ? Visibility.Visible : Visibility.Collapsed;
        bool compositionReady = wantsBackdrop
            && CompositionBackdropService.TryAttach(CompositionBackdropLayer, blurAmount: 5.5f, saturation: 1.04f, cornerRadius: 11.0f);
        CompositionBackdropService.SetVisible(CompositionBackdropLayer, compositionReady);
        SystemBackdropLayer.Visibility = wantsBackdrop && !compositionReady
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

        (Color tint, Color foreground, Color border) = Variant switch
        {
            LiquidButtonVariant.Soft => (Color.FromArgb(0x1E, 0x6C, 0x9F, 0xFF), Color.FromArgb(0xFF, 0x4E, 0x78, 0xC8), Color.FromArgb(0x62, 0xFF, 0xFF, 0xFF)),
            LiquidButtonVariant.Violet => (Color.FromArgb(0x32, 0x8C, 0x7B, 0xE8), Colors.White, Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            LiquidButtonVariant.Coral => (Color.FromArgb(0x32, 0xEA, 0x8D, 0x7C), Colors.White, Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            LiquidButtonVariant.Ghost => (Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xFF, 0x65, 0x79, 0x8E), Color.FromArgb(0x46, 0xFF, 0xFF, 0xFF)),
            // 主按钮允许更强的蓝色表面，但仍保留可见的 backdrop 和边缘层次。
            _ => (Color.FromArgb(0x78, 0x6C, 0x9F, 0xFF), Colors.White, Color.FromArgb(0x72, 0xFF, 0xFF, 0xFF)),
        };

        Surface.BorderBrush = new SolidColorBrush(border);
        Foreground = new SolidColorBrush(foreground);
        TintLayer.Background = new SolidColorBrush(tint);
        InnerRim.BorderBrush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(0x34, 0xFF, 0xFF, 0xFF), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(0x1E, 0x0C, 0x2A, 0x42), Offset = 0.55 },
                new GradientStop { Color = Color.FromArgb(0x24, 0x0C, 0x2A, 0x42), Offset = 1 },
            },
        };
    }

    private void OnClick(object sender, RoutedEventArgs args) => Click?.Invoke(this, args);
    private void OnPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _pointerInside = true;
        UpdateEdgeSpecular(args);
        MotionService.AnimateScaleXY(VisualRoot, 1.006, 1.006, 100);
    }

    private void OnPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _pointerInside = false;
        _pressed = false;
        InteractiveHighlight.Opacity = 0;
        MotionService.AnimateScaleXY(VisualRoot, 1, 1, 140);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args) => UpdateEdgeSpecular(args);

    private void OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _pointerInside = true;
        _pressed = true;
        UpdateEdgeSpecular(args);
        MotionService.AnimateScaleXY(VisualRoot, 1.015, 0.975, 85);
    }

    private void OnPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _pressed = false;
        UpdateEdgeSpecular(args);
        MotionService.AnimateScaleXY(
            VisualRoot,
            1.006,
            1.006,
            100,
            () => MotionService.AnimateScaleXY(VisualRoot, 1, 1, 130));
    }

    private void OnPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _pressed = false;
        _pointerInside = false;
        InteractiveHighlight.Opacity = 0;
        MotionService.AnimateScaleXY(VisualRoot, 1, 1, 150);
    }

    private void UpdateEdgeSpecular(PointerRoutedEventArgs args)
    {
        long now = Stopwatch.GetTimestamp();
        if (now - _lastSpecularUpdate < Stopwatch.Frequency / 60) return;
        _lastSpecularUpdate = now;
        if (HitTarget.ActualWidth <= 0 || HitTarget.ActualHeight <= 0) return;
        Windows.Foundation.Point point = args.GetCurrentPoint(HitTarget).Position;
        double x = Math.Clamp(point.X / HitTarget.ActualWidth, 0, 1);
        double y = Math.Clamp(point.Y / HitTarget.ActualHeight, 0, 1);
        double left = x;
        double right = 1 - x;
        double top = y;
        double bottom = 1 - y;
        double nearestEdge = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        double edgeProximity = Math.Clamp((0.30 - nearestEdge) / 0.30, 0, 1);

        if (nearestEdge == left)
        {
            SpecularBrush.StartPoint = new Windows.Foundation.Point(0, 0.5);
            SpecularBrush.EndPoint = new Windows.Foundation.Point(0.82, 0.5);
        }
        else if (nearestEdge == right)
        {
            SpecularBrush.StartPoint = new Windows.Foundation.Point(1, 0.5);
            SpecularBrush.EndPoint = new Windows.Foundation.Point(0.18, 0.5);
        }
        else if (nearestEdge == top)
        {
            SpecularBrush.StartPoint = new Windows.Foundation.Point(0.5, 0);
            SpecularBrush.EndPoint = new Windows.Foundation.Point(0.5, 0.82);
        }
        else
        {
            SpecularBrush.StartPoint = new Windows.Foundation.Point(0.5, 1);
            SpecularBrush.EndPoint = new Windows.Foundation.Point(0.5, 0.18);
        }

        double minimumOpacity = _pressed ? 0.035 : 0;
        double maximumOpacity = _pressed ? 0.13 : 0.075;
        InteractiveHighlight.Opacity = _pointerInside
            ? minimumOpacity + edgeProximity * (maximumOpacity - minimumOpacity)
            : 0;
    }
}
