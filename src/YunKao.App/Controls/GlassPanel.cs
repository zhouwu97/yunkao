using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YunKao.Services;

namespace YunKao.Controls;

/// <summary>
/// 液态玻璃容器：背景模糊（Composition）→ 半透明着色 → 斜向高光 → 渐变亮边 → 内侧高光边。
/// 纯色材质、系统关闭透明效果或高对比度时，切换为实体表面并停止模糊。
/// 模板见 Themes/Glass.xaml。
/// </summary>
[TemplatePart(Name = BackdropPart, Type = typeof(Border))]
[TemplatePart(Name = TintPart, Type = typeof(Border))]
[TemplatePart(Name = SolidPart, Type = typeof(Border))]
[TemplatePart(Name = SheenPart, Type = typeof(Border))]
[TemplatePart(Name = InnerRimPart, Type = typeof(Border))]
public sealed partial class GlassPanel : ContentControl
{
    private const string BackdropPart = "PART_Backdrop";
    private const string TintPart = "PART_Tint";
    private const string SolidPart = "PART_Solid";
    private const string SheenPart = "PART_Sheen";
    private const string InnerRimPart = "PART_InnerRim";

    public static readonly DependencyProperty SolidBackgroundProperty = DependencyProperty.Register(
        nameof(SolidBackground), typeof(Brush), typeof(GlassPanel), new PropertyMetadata(null));

    public static readonly DependencyProperty BlurProperty = DependencyProperty.Register(
        nameof(Blur), typeof(bool), typeof(GlassPanel), new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty BlurAmountProperty = DependencyProperty.Register(
        nameof(BlurAmount), typeof(double), typeof(GlassPanel), new PropertyMetadata(32d));

    public static readonly DependencyProperty ShowSheenProperty = DependencyProperty.Register(
        nameof(ShowSheen), typeof(bool), typeof(GlassPanel), new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty TintScaleProperty = DependencyProperty.Register(
        nameof(TintScale), typeof(double), typeof(GlassPanel), new PropertyMetadata(1d, OnVisualPropertyChanged));

    public static readonly DependencyProperty ElevationProperty = DependencyProperty.Register(
        nameof(Elevation), typeof(double), typeof(GlassPanel), new PropertyMetadata(0d, OnVisualPropertyChanged));

    private Border? _backdrop;
    private Border? _tint;
    private Border? _solid;
    private Border? _sheen;
    private Border? _innerRim;
    private bool _blurAttached;
    private bool _subscribed;

    public GlassPanel()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RegisterPropertyChangedCallback(CornerRadiusProperty, (_, _) => ApplyGeometry());
    }

    /// <summary>实体模式下使用的不透明背景。</summary>
    public Brush? SolidBackground
    {
        get => (Brush?)GetValue(SolidBackgroundProperty);
        set => SetValue(SolidBackgroundProperty, value);
    }

    /// <summary>是否对背后内容做实时模糊。小面积或嵌套面板应关闭，交给父级玻璃。</summary>
    public bool Blur
    {
        get => (bool)GetValue(BlurProperty);
        set => SetValue(BlurProperty, value);
    }

    public double BlurAmount
    {
        get => (double)GetValue(BlurAmountProperty);
        set => SetValue(BlurAmountProperty, value);
    }

    public bool ShowSheen
    {
        get => (bool)GetValue(ShowSheenProperty);
        set => SetValue(ShowSheenProperty, value);
    }

    /// <summary>着色层不透明度系数（与全局透明强度相乘）。</summary>
    public double TintScale
    {
        get => (double)GetValue(TintScaleProperty);
        set => SetValue(TintScaleProperty, value);
    }

    /// <summary>投影高度（DIP），0 表示不投影。</summary>
    public double Elevation
    {
        get => (double)GetValue(ElevationProperty);
        set => SetValue(ElevationProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _backdrop = GetTemplateChild(BackdropPart) as Border;
        _tint = GetTemplateChild(TintPart) as Border;
        _solid = GetTemplateChild(SolidPart) as Border;
        _sheen = GetTemplateChild(SheenPart) as Border;
        _innerRim = GetTemplateChild(InnerRimPart) as Border;
        _blurAttached = false;
        ApplyGeometry();
        ApplyMaterial();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs args)
    {
        if (d is GlassPanel panel) panel.ApplyMaterial();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_subscribed)
        {
            GlassSettings.Changed += OnGlassSettingsChanged;
            _subscribed = true;
        }
        ApplyMaterial();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_subscribed) return;
        GlassSettings.Changed -= OnGlassSettingsChanged;
        _subscribed = false;
    }

    private void OnGlassSettingsChanged(object? sender, EventArgs args)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyMaterial();
        else DispatcherQueue.TryEnqueue(ApplyMaterial);
    }

    private void ApplyGeometry()
    {
        if (_innerRim is null) return;
        CornerRadius radius = CornerRadius;
        _innerRim.CornerRadius = new CornerRadius(
            Math.Max(0, radius.TopLeft - 1),
            Math.Max(0, radius.TopRight - 1),
            Math.Max(0, radius.BottomRight - 1),
            Math.Max(0, radius.BottomLeft - 1));
    }

    private void ApplyMaterial()
    {
        if (_tint is null || _solid is null) return;

        bool solid = GlassSettings.IsSolid;
        _solid.Visibility = solid ? Visibility.Visible : Visibility.Collapsed;
        _tint.Opacity = solid ? 0 : Math.Clamp(GlassSettings.TintOpacity * TintScale, 0, 1);
        if (_sheen is not null)
        {
            _sheen.Visibility = !solid && ShowSheen ? Visibility.Visible : Visibility.Collapsed;
        }

        bool wantsBlur = !solid && Blur && IsLoaded;
        if (_backdrop is not null)
        {
            if (wantsBlur && !_blurAttached)
            {
                _blurAttached = CompositionBackdropService.TryAttach(
                    _backdrop,
                    blurAmount: (float)BlurAmount,
                    saturation: 1.45f,
                    cornerRadius: (float)CornerRadius.TopLeft);
            }
            if (_blurAttached) CompositionBackdropService.SetVisible(_backdrop, wantsBlur);
        }

        ApplyElevation();
    }

    private void ApplyElevation()
    {
        if (Elevation > 0 && !GlassSettings.IsHighContrast)
        {
            Translation = new Vector3(0, 0, (float)Elevation);
            Shadow ??= new ThemeShadow();
        }
        else
        {
            Translation = Vector3.Zero;
            Shadow = null;
        }
    }
}
