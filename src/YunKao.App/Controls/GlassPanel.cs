using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YunKao.Services;

namespace YunKao.Controls;

/// <summary>A content container backed by LiquidGlassSurface, with an opaque accessibility fallback.</summary>
[TemplatePart(Name = "PART_Surface", Type = typeof(LiquidGlassSurface))]
public sealed partial class GlassPanel : ContentControl
{
    public static readonly DependencyProperty SolidBackgroundProperty = Register(nameof(SolidBackground), typeof(Brush), null);
    public static readonly DependencyProperty BackdropSourceProperty = Register(nameof(BackdropSource), typeof(FrameworkElement), null);
    public static readonly DependencyProperty BlurProperty = Register(nameof(Blur), typeof(bool), true);
    public static readonly DependencyProperty BlurAmountProperty = Register(nameof(BlurAmount), typeof(double), .25d);
    public static readonly DependencyProperty RefractionAmountProperty = Register(nameof(RefractionAmount), typeof(double), 28d);
    public static readonly DependencyProperty SaturationProperty = Register(nameof(Saturation), typeof(double), 1.12d);
    public static readonly DependencyProperty DispersionProperty = Register(nameof(Dispersion), typeof(double), .35d);
    public static readonly DependencyProperty ShowSheenProperty = Register(nameof(ShowSheen), typeof(bool), true);
    public static readonly DependencyProperty TintScaleProperty = Register(nameof(TintScale), typeof(double), 1d);
    public static readonly DependencyProperty ElevationProperty = DependencyProperty.Register(
        nameof(Elevation), typeof(double), typeof(GlassPanel), new PropertyMetadata(0d, OnElevationChanged));
    private bool _subscribed;

    public Brush? SolidBackground { get => (Brush?)GetValue(SolidBackgroundProperty); set => SetValue(SolidBackgroundProperty, value); }
    /// <summary>A separate decorative element. Ancestors containing this panel are rejected to prevent capture feedback.</summary>
    public FrameworkElement? BackdropSource { get => (FrameworkElement?)GetValue(BackdropSourceProperty); set => SetValue(BackdropSourceProperty, value); }
    public bool Blur { get => (bool)GetValue(BlurProperty); set => SetValue(BlurProperty, value); }
    public double BlurAmount { get => (double)GetValue(BlurAmountProperty); set => SetValue(BlurAmountProperty, value); }
    public double RefractionAmount { get => (double)GetValue(RefractionAmountProperty); set => SetValue(RefractionAmountProperty, value); }
    public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
    public double Dispersion { get => (double)GetValue(DispersionProperty); set => SetValue(DispersionProperty, value); }
    public bool ShowSheen { get => (bool)GetValue(ShowSheenProperty); set => SetValue(ShowSheenProperty, value); }
    public double TintScale { get => (double)GetValue(TintScaleProperty); set => SetValue(TintScaleProperty, value); }
    public double Elevation { get => (double)GetValue(ElevationProperty); set => SetValue(ElevationProperty, value); }

    private static DependencyProperty Register(string name, Type type, object? value)
        => DependencyProperty.Register(name, type, typeof(GlassPanel), new PropertyMetadata(value));

    public GlassPanel()
    {
        Loaded += (_, _) =>
        {
            if (!_subscribed) { GlassSettings.Changed += OnMaterialChanged; _subscribed = true; }
            ApplyElevation();
        };
        Unloaded += (_, _) =>
        {
            GlassSettings.Changed -= OnMaterialChanged;
            _subscribed = false;
        };
    }

    private static void OnElevationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((GlassPanel)sender).ApplyElevation();

    private void OnMaterialChanged(object? sender, EventArgs args)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyElevation();
        else DispatcherQueue.TryEnqueue(ApplyElevation);
    }

    private void ApplyElevation()
    {
        float elevation = double.IsFinite(Elevation) ? (float)Math.Clamp(Elevation, 0, 64) : 0;
        if (elevation > 0 && !GlassSettings.IsHighContrast)
        {
            Translation = new Vector3(0, 0, elevation);
            Shadow ??= new ThemeShadow();
        }
        else { Translation = Vector3.Zero; Shadow = null; }
    }
}
