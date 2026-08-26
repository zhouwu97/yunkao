using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YunKao.Services;
using Windows.UI;

namespace YunKao.Controls;

public enum LiquidCardVariant
{
    Neutral,
    Blue,
    Violet,
    Coral,
    Amber,
}

/// <summary>
/// 使用 Composition backdrop、弱 tint、双层 rim 和软阴影组成的中性玻璃卡片。
/// </summary>
public sealed partial class LiquidCard : UserControl
{
    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant),
        typeof(LiquidCardVariant),
        typeof(LiquidCard),
        new PropertyMetadata(LiquidCardVariant.Neutral, OnVariantChanged));

    public static readonly DependencyProperty BackdropModeProperty = DependencyProperty.Register(
        nameof(BackdropMode),
        typeof(LiquidBackdropMode),
        typeof(LiquidCard),
        new PropertyMetadata(LiquidBackdropMode.System, OnBackdropModeChanged));

    public LiquidCard()
    {
        InitializeComponent();
        Padding = new Thickness(16);
        Loaded += (_, _) =>
        {
            ApplyVariant();
            ApplyBackdropMode();
            MotionService.SetSoftShadow(GlassRoot, enabled: true);
        };
    }

    public LiquidCardVariant Variant
    {
        get => (LiquidCardVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public LiquidBackdropMode BackdropMode
    {
        get => (LiquidBackdropMode)GetValue(BackdropModeProperty);
        set => SetValue(BackdropModeProperty, value);
    }

    private static void OnVariantChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is LiquidCard card) card.ApplyVariant();
    }

    private static void OnBackdropModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is LiquidCard card) card.ApplyBackdropMode();
    }

    private void ApplyBackdropMode()
    {
        if (CompositionBackdropLayer is null || SystemBackdropLayer is null) return;

        bool wantsBackdrop = BackdropMode == LiquidBackdropMode.System;
        CompositionBackdropLayer.Visibility = wantsBackdrop ? Visibility.Visible : Visibility.Collapsed;
        bool compositionReady = wantsBackdrop
            && CompositionBackdropService.TryAttach(CompositionBackdropLayer, blurAmount: 7.0f, saturation: 1.03f, cornerRadius: 18.0f);
        CompositionBackdropService.SetVisible(CompositionBackdropLayer, compositionReady);
        SystemBackdropLayer.Visibility = wantsBackdrop && !compositionReady
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyVariant()
    {
        if (TintLayer is null) return;

        Color tint = Variant switch
        {
            LiquidCardVariant.Blue => Color.FromArgb(0x12, 0x6C, 0x9F, 0xFF),
            LiquidCardVariant.Violet => Color.FromArgb(0x12, 0x8C, 0x7B, 0xE8),
            LiquidCardVariant.Coral => Color.FromArgb(0x12, 0xEA, 0x8D, 0x7C),
            LiquidCardVariant.Amber => Color.FromArgb(0x14, 0xD8, 0xA4, 0x51),
            _ => Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF),
        };

        TintLayer.Background = new SolidColorBrush(tint);
        InnerRim.BorderBrush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(0x22, 0x0C, 0x2A, 0x42), Offset = 0.55 },
                new GradientStop { Color = Color.FromArgb(0x2A, 0x0C, 0x2A, 0x42), Offset = 1 },
            },
        };
    }
}
