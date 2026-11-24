using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace YunKao.Services;

/// <summary>Live blur fallback when a standalone panel has no separate refraction source.</summary>
public static class CompositionBackdropService
{
    private static readonly ConditionalWeakTable<FrameworkElement, GlassState> States = new();

    public static bool TryAttach(FrameworkElement host, float blurAmount = 7, float saturation = 1.03f, float cornerRadius = 18)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return false;
        return TryAttachWithWin2D(host, blurAmount, saturation, cornerRadius);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryAttachWithWin2D(FrameworkElement host, float blur, float saturation, float radius)
    {
        try
        {
            if (!States.TryGetValue(host, out var state))
            {
                Compositor compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
                using var effect = new SaturationEffect
                {
                    Name = "Saturation", Saturation = 1,
                    Source = new GaussianBlurEffect
                    {
                        Name = "Blur", BlurAmount = 7, BorderMode = EffectBorderMode.Hard,
                        Optimization = EffectOptimization.Balanced, Source = new CompositionEffectSourceParameter("backdrop"),
                    },
                };
                using var factory = compositor.CreateEffectFactory(effect, new[] { "Blur.BlurAmount", "Saturation.Saturation" });
                var brush = factory.CreateBrush();
                var backdrop = compositor.CreateBackdropBrush();
                brush.SetSourceParameter("backdrop", backdrop);
                var visual = compositor.CreateSpriteVisual();
                visual.Brush = brush;
                var geometry = compositor.CreateRoundedRectangleGeometry();
                var clip = compositor.CreateGeometricClip(geometry);
                visual.Clip = clip;
                state = new GlassState(host, visual, geometry, clip, brush, backdrop);
                States.Add(host, state);
                ElementCompositionPreview.SetElementChildVisual(host, visual);
            }
            state.Radius = float.IsFinite(radius) ? Math.Max(0, radius) : 0;
            state.Brush.Properties.InsertScalar("Blur.BlurAmount", float.IsFinite(blur) ? Math.Clamp(blur, 0, 64) : 9);
            state.Brush.Properties.InsertScalar("Saturation.Saturation", float.IsFinite(saturation) ? Math.Clamp(saturation, 0, 2) : 1);
            state.Visual.IsVisible = true;
            state.Update();
            return true;
        }
        catch
        {
            Detach(host);
            return false;
        }
    }

    public static void SetVisible(FrameworkElement host, bool visible)
    {
        if (States.TryGetValue(host, out var state)) state.Visual.IsVisible = visible;
    }

    public static void Detach(FrameworkElement host)
    {
        if (!States.TryGetValue(host, out var state)) return;
        States.Remove(host);
        state.Dispose();
    }

    private sealed class GlassState : IDisposable
    {
        private readonly FrameworkElement _host;
        private readonly CompositionGeometricClip _clip;
        private readonly CompositionBackdropBrush _backdrop;
        public SpriteVisual Visual { get; }
        public CompositionRoundedRectangleGeometry Geometry { get; }
        public CompositionEffectBrush Brush { get; }
        public float Radius { get; set; }

        public GlassState(FrameworkElement host, SpriteVisual visual, CompositionRoundedRectangleGeometry geometry,
            CompositionGeometricClip clip, CompositionEffectBrush brush, CompositionBackdropBrush backdrop)
        {
            _host = host; Visual = visual; Geometry = geometry; _clip = clip; Brush = brush; _backdrop = backdrop;
            host.SizeChanged += OnSizeChanged;
            host.Unloaded += OnUnloaded;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Update();
        private void OnUnloaded(object sender, RoutedEventArgs args) => Detach(_host);

        public void Update()
        {
            var size = new Vector2(Math.Max(0, (float)_host.ActualWidth), Math.Max(0, (float)_host.ActualHeight));
            Visual.Size = Geometry.Size = size;
            Geometry.CornerRadius = new Vector2(Math.Min(Radius, Math.Min(size.X, size.Y) / 2));
        }

        public void Dispose()
        {
            _host.SizeChanged -= OnSizeChanged;
            _host.Unloaded -= OnUnloaded;
            ElementCompositionPreview.SetElementChildVisual(_host, null);
            Visual.Dispose(); _clip.Dispose(); Geometry.Dispose(); Brush.Dispose(); _backdrop.Dispose();
        }
    }
}
