using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using YunKao.Core.Presentation;
using YunKao.Services;

namespace YunKao.Controls;

/// <summary>A passive glass layer; native controls above it retain pointer, keyboard and automation behavior.</summary>
public sealed partial class LiquidGlassSurface : Grid
{
    private const float DeformationPadding = 28;
    private readonly Grid _optics = new() { IsHitTestVisible = false, Margin = new Thickness(-DeformationPadding) };
    private readonly Border _wash = new() { IsHitTestVisible = false };
    private LiquidGlassRenderer? _renderer;
    private LiquidGlassScene? _scene, _ownedScene, _connectedScene;
    private UIElement? _connectedInput;
    private LiquidGlassInteraction? _interaction;
    private bool _subscribed;
    private (Point Origin, Point X, Point Y)? _position;

    // Material geometry must not use Grid.CornerRadius: that also clips the overflowing optical layer.
    public static readonly DependencyProperty SurfaceCornersProperty = Register(nameof(SurfaceCorners), typeof(CornerRadius), new CornerRadius());
    public CornerRadius SurfaceCorners { get => (CornerRadius)GetValue(SurfaceCornersProperty); set => SetValue(SurfaceCornersProperty, value); }

    public static readonly DependencyProperty TintProperty = Register(nameof(Tint), typeof(Brush), null);
    public static readonly DependencyProperty SolidBackgroundProperty = Register(nameof(SolidBackground), typeof(Brush), null);
    public static readonly DependencyProperty BackdropSourceProperty = Register(nameof(BackdropSource), typeof(FrameworkElement), null);
    public static readonly DependencyProperty BlurProperty = Register(nameof(Blur), typeof(bool), true);
    public static readonly DependencyProperty BlurAmountProperty = Register(nameof(BlurAmount), typeof(double), .25d);
    public static readonly DependencyProperty RefractionAmountProperty = Register(nameof(RefractionAmount), typeof(double), 28d);
    public static readonly DependencyProperty SaturationProperty = Register(nameof(Saturation), typeof(double), 1.12d);
    public static readonly DependencyProperty DispersionProperty = Register(nameof(Dispersion), typeof(double), .35d);
    public static readonly DependencyProperty TintScaleProperty = Register(nameof(TintScale), typeof(double), 1d);
    public static readonly DependencyProperty ShowSheenProperty = Register(nameof(ShowSheen), typeof(bool), true);
    public static readonly DependencyProperty FallbackOpacityProperty = DependencyProperty.Register(
        nameof(FallbackOpacity), typeof(double), typeof(LiquidGlassSurface), new PropertyMetadata(1d));

    public double FallbackOpacity { get => (double)GetValue(FallbackOpacityProperty); private set => SetValue(FallbackOpacityProperty, value); }

    public Brush? Tint { get => (Brush?)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public Brush? SolidBackground { get => (Brush?)GetValue(SolidBackgroundProperty); set => SetValue(SolidBackgroundProperty, value); }
    public FrameworkElement? BackdropSource { get => (FrameworkElement?)GetValue(BackdropSourceProperty); set => SetValue(BackdropSourceProperty, value); }
    public bool Blur { get => (bool)GetValue(BlurProperty); set => SetValue(BlurProperty, value); }
    public double BlurAmount { get => (double)GetValue(BlurAmountProperty); set => SetValue(BlurAmountProperty, value); }
    public double RefractionAmount { get => (double)GetValue(RefractionAmountProperty); set => SetValue(RefractionAmountProperty, value); }
    public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
    public double Dispersion { get => (double)GetValue(DispersionProperty); set => SetValue(DispersionProperty, value); }
    public double TintScale { get => (double)GetValue(TintScaleProperty); set => SetValue(TintScaleProperty, value); }
    public bool ShowSheen { get => (bool)GetValue(ShowSheenProperty); set => SetValue(ShowSheenProperty, value); }

    private static DependencyProperty Register(string name, Type type, object? value)
        => DependencyProperty.Register(name, type, typeof(LiquidGlassSurface), new PropertyMetadata(value, OnMaterialPropertyChanged));

    private static void OnMaterialPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var surface = (LiquidGlassSurface)sender;
        if (args.Property == BackdropSourceProperty)
        {
            surface.ReleaseRenderer();
            surface._ownedScene?.Dispose();
            surface._ownedScene = null;
        }
        surface.UpdateMaterial();
    }

    public LiquidGlassSurface()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Children.Add(_wash);
        Children.Add(_optics);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => UpdateMaterial();
        SizeChanged += (_, _) => UpdateMaterial();

    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_subscribed) return;
        _subscribed = true;
        GlassSettings.Changed += OnMaterialChanged;
        LayoutUpdated += OnLayoutUpdated;
        AttachInput(_connectedInput ?? FindInput());
        UpdateMaterial();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_subscribed) return;
        _subscribed = false;
        GlassSettings.Changed -= OnMaterialChanged;
        LayoutUpdated -= OnLayoutUpdated;
        AttachInput(null);
        ReleaseRenderer();
        _ownedScene?.Dispose();
        _ownedScene = _scene = null;
        _position = null;
    }

    internal void Connect(LiquidGlassScene scene, UIElement input)
    {
        if (!ReferenceEquals(_connectedScene, scene)) ReleaseRenderer();
        _connectedScene = scene;
        _connectedInput = input;
        if (IsLoaded) AttachInput(input);
        UpdateMaterial();
    }

    internal void RefreshPosition() => _renderer?.Invalidate();

    private void AttachInput(UIElement? input)
    {
        if (ReferenceEquals(_interaction?.Input, input)) return;
        _interaction?.Dispose();
        _interaction = input is null ? null : new LiquidGlassInteraction(this, input,
            () => _renderer is { HasFailed: false } && !GlassSettings.IsSolid, PaintInteraction);
    }

    private UIElement? FindInput()
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase button) return button;
            if (node is GlassPanel { Content: not null } panel) return panel;
        }
        return VisualTreeHelper.GetParent(this) as UIElement;
    }

    private void PaintInteraction(LiquidSurfaceMotion motion, double x, double y)
    {
        _renderer?.SetInteraction(motion.Pressure, x, y);
        _renderer?.SetJellyPose(motion.Stretch, motion.Angle, motion.Lift, motion.BendX, motion.BendY, motion.Energy);
    }

    private void OnMaterialChanged(object? sender, EventArgs args)
    {
        void Refresh()
        {
            if (_renderer?.HasFailed == true) ReleaseRenderer();
            UpdateMaterial();
        }
        if (DispatcherQueue.HasThreadAccess) Refresh();
        else DispatcherQueue.TryEnqueue(Refresh);
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        if (_renderer is null || _scene is null || !_scene.Element.IsLoaded) return;
        var transform = TransformToVisual(_scene.Element);
        var next = (transform.TransformPoint(new Point()), transform.TransformPoint(new Point(ActualWidth, 0)),
            transform.TransformPoint(new Point(0, ActualHeight)));
        if (_position == next) return;
        _position = next;
        _renderer.Invalidate();
    }

    private void UpdateMaterial()
    {
        if (!IsLoaded) return;
        _wash.CornerRadius = SurfaceCorners;
        _wash.Background = SolidBackground ?? Tint ?? new SolidColorBrush(
            ActualTheme == ElementTheme.Dark ? Color.FromArgb(255, 54, 50, 72) : Color.FromArgb(255, 255, 255, 255));
        if (_wash.Background is SolidColorBrush fallback && fallback.Color.A < 255)
        {
            Color color = fallback.Color;
            color.A = 255;
            _wash.Background = new SolidColorBrush(color);
        }
        if (GlassSettings.IsSolid || ActualWidth < 2 || ActualHeight < 2
            || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            ReleaseRenderer();
            return;
        }
        LiquidGlassScene? scene;
        if (BackdropSource is { } source && !ReferenceEquals(source, this) && !IsAncestor(source))
            scene = _ownedScene ??= new LiquidGlassScene(source);
        else scene = _connectedScene ?? LiquidGlassScope.Find(this);
        if (scene?.IsDisposed == true) scene = null;
        if (!ReferenceEquals(scene, _scene)) ReleaseRenderer();
        _scene = scene;
        if (_scene is null)
        {
            _optics.Margin = new Thickness(0);
            bool blurred = Blur && CompositionBackdropService.TryAttach(_optics,
                Clamp(BlurAmount, 0, 64, 1.5f), Clamp(Saturation, 0, 2, 1.12f), (float)SurfaceCorners.TopLeft);
            if (!blurred) CompositionBackdropService.Detach(_optics);
            _wash.Opacity = blurred ? Math.Clamp(GlassSettings.TintOpacity * TintScale, 0, 1) : 1;
            return;
        }
        try
        {
            _optics.Margin = new Thickness(-DeformationPadding);
            _renderer ??= new LiquidGlassRenderer(_optics, _scene, 0,
                materialReady: ready => _wash.Opacity = FallbackOpacity = ready ? 0 : 1);
            CornerRadius c = SurfaceCorners;
            Color tint = Tint is SolidColorBrush brush ? brush.Color
                : ActualTheme == ElementTheme.Dark ? Color.FromArgb(255, 42, 39, 61) : Color.FromArgb(255, 255, 255, 255);
            _renderer.SetMaterial(new(
                new GlassCorners((float)c.TopLeft, (float)c.TopRight, (float)c.BottomRight, (float)c.BottomLeft),
                Blur ? Clamp(BlurAmount, 0, 64, 1.5f) : 0,
                Clamp(RefractionAmount, 0, 80, 24), Clamp(Saturation, 0, 2, 1.12f), tint,
                Clamp(GlassSettings.TintOpacity * TintScale, 0, 1, .6f), ShowSheen,
                Padding: DeformationPadding, Dispersion: Clamp(Dispersion, 0, 1, .35f)));
            _interaction?.Paint();
        }
        catch (Exception ex)
        {
            ReleaseRenderer();
            App.Services.Diagnostics.Warning("面板玻璃不可用：" + ex.Message);
        }
    }

    // Sampling an ancestor would include this lens and feed the previous frame back into itself.
    private bool IsAncestor(FrameworkElement source)
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, source)) return true;
        return false;
    }

    private static float Clamp(double value, float min, float max, float fallback)
        => double.IsFinite(value) ? (float)Math.Clamp(value, min, max) : fallback;

    private void ReleaseRenderer()
    {
        _interaction?.Reset();
        CompositionBackdropService.Detach(_optics);
        _renderer?.Dispose();
        _renderer = null;
        _wash.Opacity = FallbackOpacity = 1;
        _position = null;
    }
}
