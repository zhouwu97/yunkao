using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace YunKao.Controls;

public enum StatusPillVariant
{
    Neutral,
    Cyan,
    Violet,
    Coral,
    Amber,
}

public sealed partial class StatusPill : UserControl
{
    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant),
        typeof(StatusPillVariant),
        typeof(StatusPill),
        new PropertyMetadata(StatusPillVariant.Neutral, OnVariantChanged));

    public StatusPill()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyVariant();
    }

    public StatusPillVariant Variant
    {
        get => (StatusPillVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private static void OnVariantChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is StatusPill pill) pill.ApplyVariant();
    }

    private void ApplyVariant()
    {
        if (Surface is null) return;
        (string background, string foreground) = Variant switch
        {
            StatusPillVariant.Cyan => ("#52CDEFF2", "#267D91"),
            StatusPillVariant.Violet => ("#4AEEE9FF", "#6656B3"),
            StatusPillVariant.Coral => ("#4AFBE8E3", "#A65C50"),
            StatusPillVariant.Amber => ("#4AF7EAC2", "#916B22"),
            _ => ("#44F8FBFE", "#65798E"),
        };

        Surface.Background = new SolidColorBrush((Windows.UI.Color)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Windows.UI.Color), background));
        Foreground = new SolidColorBrush((Windows.UI.Color)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Windows.UI.Color), foreground));
    }
}
