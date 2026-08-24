using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
/// 使用局部 Desktop Acrylic 采样、色彩 tint、内描边和镜面高光组成的液态玻璃卡片。
/// </summary>
public sealed partial class LiquidCard : UserControl
{
    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant),
        typeof(LiquidCardVariant),
        typeof(LiquidCard),
        new PropertyMetadata(LiquidCardVariant.Neutral, OnVariantChanged));

    public LiquidCard()
    {
        InitializeComponent();
        Padding = new Thickness(16);
        Loaded += (_, _) => ApplyVariant();
    }

    public LiquidCardVariant Variant
    {
        get => (LiquidCardVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private static void OnVariantChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is LiquidCard card) card.ApplyVariant();
    }

    private void ApplyVariant()
    {
        if (TintLayer is null) return;

        (Color start, Color end) = Variant switch
        {
            LiquidCardVariant.Blue => (Color.FromArgb(0x26, 0x6C, 0x9F, 0xFF), Color.FromArgb(0x16, 0x62, 0xB7, 0xD8)),
            LiquidCardVariant.Violet => (Color.FromArgb(0x28, 0x8C, 0x7B, 0xE8), Color.FromArgb(0x12, 0xD7, 0x92, 0xB7)),
            LiquidCardVariant.Coral => (Color.FromArgb(0x28, 0xEA, 0x8D, 0x7C), Color.FromArgb(0x10, 0xD8, 0xA4, 0x51)),
            LiquidCardVariant.Amber => (Color.FromArgb(0x2A, 0xD8, 0xA4, 0x51), Color.FromArgb(0x12, 0xEA, 0x8D, 0x7C)),
            _ => (Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0C, 0x62, 0xB7, 0xD8)),
        };

        TintLayer.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = start, Offset = 0 },
                new GradientStop { Color = Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF), Offset = 0.48 },
                new GradientStop { Color = end, Offset = 1 },
            },
        };
    }
}
